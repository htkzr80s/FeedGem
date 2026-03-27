using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using FeedGem.Models;
using FeedGem.Data;

namespace FeedGem
{
    public partial class MainWindow : Window
    {
        #region --- フィールド定義 ---

        // データベース接続用の文字列
        private readonly string dbConnectionString = "Data Source=feedgem.db";

        // HTTP通信用のクライアントインスタンスを生成
        // アプリケーション全体で再利用してリソースの枯渇を防ぐ
        private static readonly HttpClient httpClient = new HttpClient();

        // 記事リスト（中央ペイン）に表示するためのデータ管理用
        private ObservableCollection<ArticleItem> currentArticles = new ObservableCollection<ArticleItem>();

        // データベース操作を専門に行うインスタンス
        private readonly FeedRepository _repository;
        #endregion

        #region --- 初期設定系 ---
        public MainWindow()
        {
            InitializeComponent();
            SetupWindowIcon();

            // リポジトリを初期化（ファイルパスを指定）
            _repository = new FeedRepository("feedgem.db");
            _repository.Initialize(); // 旧 InitializeDatabase() の代わり

            // 起動時にデータを画面に反映させる
            _ = LoadFeedsToTreeViewAsync();

            // バックグラウンドでの更新処理を開始
            _ = UpdateAllFeedsAsync();
            _ = StartBackgroundPollingAsync();
        }

        // 高DPIアイコンをウィンドウに適用する
        private void SetupWindowIcon()
        {
            var icon = App.GetHighDpiIcon();
            if (icon != null)
            {
                this.Icon = icon;
            }
        }

        // SQLiteデータベースと必要なテーブルを初期化する
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(dbConnectionString);
            connection.Open();

            // feedsテーブルとentriesテーブルを作成
            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS feeds (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    folder_path TEXT NOT NULL,
                    title TEXT NOT NULL,
                    url TEXT UNIQUE NOT NULL
                );
                CREATE TABLE IF NOT EXISTS entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    feed_id INTEGER,
                    title TEXT NOT NULL,
                    url TEXT UNIQUE NOT NULL,
                    summary TEXT,
                    published_date TEXT,
                    is_read INTEGER DEFAULT 0,
                    FOREIGN KEY(feed_id) REFERENCES feeds(id)
                );";

            using var command = new SqliteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();
        }
        #endregion

        #region --- UIイベントハンドラ ---

        // リスト内の記事選択が変更された際のイベントハンドラ
        private void ArticleListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 選択項目がArticleItem型であるか判定
            if (ArticleListView.SelectedItem is ArticleItem selectedArticle)
            {
                // プレビュー用のHTML文字列を生成
                string html = $@"
                    <html>
                    <head><meta charset='utf-8'></head>
                    <body style='font-family: sans-serif; line-height: 1.6; padding: 15px;'>
                        <h2 style='border-bottom: 1px solid #ccc; padding-bottom: 5px;'>{selectedArticle.Title}</h2>
                        <div style='font-size: 14px;'>{selectedArticle.Summary}</div>
                    </body>
                    </html>";

                // WebBrowserコントロールへHTMLを流し込む
                PreviewBrowser.NavigateToString(html);
            }
        }

        // ブラウザで開くボタン押下時のイベントハンドラ
        private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            // 選択項目がArticleItem型であり、URLが存在するか判定
            if (ArticleListView.SelectedItem is ArticleItem selectedArticle && !string.IsNullOrEmpty(selectedArticle.Url))
            {
                // 既定のブラウザで対象URLを開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectedArticle.Url, // 開くURLを指定
                    UseShellExecute = true          // OSのシェル機能を使用して起動
                });
            }
        }

       // 入力欄にマウスやタブで移動した時の処理
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "URLを入力してEnter...")
            {
                SearchBox.Text = ""; // ヒント文字を消す
                SearchBox.Foreground = Brushes.Black; // 文字色を黒にする
            }
        }

        // テキストボックスでキーが押された時の処理
        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Enterキーが押されたか確認
            if (e.Key == Key.Enter)
            {
                string url = SearchBox.Text.Trim();
                // ヒント文字ではない、かつ空でない場合のみ実行
                if (!string.IsNullOrEmpty(url) && url != "URLを入力してEnter...")
                {
                    SearchBox.IsEnabled = false; // 処理中は入力を無効化
                    await DiscoverAndAddFeedAsync(url); // フィード探索を開始
                    SearchBox.IsEnabled = true;
                    SearchBox.Text = ""; // 終わったら入力欄を空にする
                }
            }
        }
        #endregion

        #region --- データ取得・巡回系 ---

        // バックグラウンドでの定期巡回タスクを開始する
        private async Task StartBackgroundPollingAsync()
        {
            // 1時間間隔の非同期タイマーを生成
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

            // タイマーのチック発生ごとにループを実行
            while (await timer.WaitForNextTickAsync())
            {
                await UpdateAllFeedsAsync();
            }
        }

        // すべてのフィードを巡回して最新記事を取得・保存する
        private async Task UpdateAllFeedsAsync()
        {
            // 登録されている全フィードを取得
            var feeds = await _repository.GetAllFeedsAsync();

            foreach (var feed in feeds)
            {
                try
                {
                    // インターネットからフィードをダウンロード
                    using var reader = XmlReader.Create(feed.Url);
                    var rssData = SyndicationFeed.Load(reader);

                    // 記事を一つずつチェックして保存
                    foreach (var item in rssData.Items)
                    {
                        string title = item.Title.Text;
                        string url = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";
                        string summary = item.Summary?.Text ?? "";
                        string pubDate = item.PublishDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

                        // リポジトリ経由でデータベースに保存（重複はリポジトリ側で無視される）
                        await _repository.SaveEntryAsync(feed.Id, title, url, summary, pubDate);
                    }
                }
                catch (Exception ex)
                {
                    // 通信エラーなどはデバッグ出力に記録して次へ進む
                    Debug.WriteLine($"フィード更新失敗: {feed.Title} - {ex.Message}");
                }
            }

            // 画面上の最終更新時刻を更新
            await Dispatcher.InvokeAsync(() =>
            {
                LastUpdateTextBlock.Text = $"最終更新: {DateTime.Now:HH:mm:ss}";
            });
        }

        // 入力されたURLからRSSフィードを探索し、追加処理を行う
        private async Task DiscoverAndAddFeedAsync(string targetUrl)
        {
            try
            {
                // URLから直接フィードを読み込む（Atom/RSS両対応）
                using var response = await httpClient.GetAsync(targetUrl);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var xmlReader = XmlReader.Create(stream);
                    var feed = SyndicationFeed.Load(xmlReader);

                    // 読み込めたら即座に追加
                    await AddFeedToDatabaseAsync("/", feed.Title?.Text ?? "Untitled Feed", targetUrl);
                    await LoadFeedsToTreeViewAsync();
                    return; // 成功したらここで終了
                }
            }
            catch (XmlException)
            {
                // XmlException発生時、対象URLはHTMLページであると判定して後続の探索処理へ移行
            }
            catch (Exception)
            {
                SearchBox.Text = "エラー: アクセスできません";
                return;
            }

            // HTMLページからフィードリンクを抽出する
            var htmlWeb = new HtmlWeb();
            var doc = await htmlWeb.LoadFromWebAsync(targetUrl);
            var nodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate' and (contains(@type, 'rss') or contains(@type, 'atom'))]");

            // 候補ノードの存在判定
            if (nodes == null || nodes.Count == 0)
            {
                SearchBox.Text = "フィードが見つかりません";
                return;
            }

            // 候補が7個以上の場合は仕様に基づき処理を中断
            if (nodes.Count >= 7)
            {
                SearchBox.Text = $"候補多数({nodes.Count}件)のため中断";
                return;
            }

            // 候補が1〜6個の場合、ウィンドウを表示して選択させる
            var candidates = new List<FeedCandidate>();
            foreach (var node in nodes)
            {
                string feedUrl = node.GetAttributeValue("href", "");
                string feedTitle = node.GetAttributeValue("title", "Found Feed");

                // 相対パスの補完
                if (!feedUrl.StartsWith("http") && Uri.TryCreate(new Uri(targetUrl), feedUrl, out Uri? absoluteUri))
                {
                    feedUrl = absoluteUri.ToString();
                }

                candidates.Add(new FeedCandidate { Title = feedTitle, Url = feedUrl });
            }

            // UIスレッドで選択ウィンドウを表示
            await Dispatcher.Invoke(async () =>
            {
                var selectionWindow = new FeedSelectionWindow(candidates) { Owner = this };

                // ユーザーが選択して「OK」を押したか確認
                if (selectionWindow.ShowDialog() == true)
                {
                    foreach (var selected in selectionWindow.SelectedFeeds)
                    {
                        // データベースへ登録
                        await AddFeedToDatabaseAsync("/", selected.Title, selected.Url);
                    }
                    // 登録が終わったら、左側のツリービューを最新の状態にする
                    await LoadFeedsToTreeViewAsync();
                }
            });
        }
        #endregion

        #region --- DBアクセス ---

        // データベースから「購読一覧」を取得して左ペインに表示する
        private async Task LoadFeedsToTreeViewAsync()
        {
            FeedTreeView.Items.Clear();
            var rootNode = new TreeViewItem { Header = "すべての購読", IsExpanded = true };
            FeedTreeView.Items.Add(rootNode);

            var feeds = await _repository.GetAllFeedsAsync();
            foreach (var feed in feeds)
            {
                var feedNode = new TreeViewItem { Header = feed.Title, Tag = feed.Id };
                feedNode.Selected += async (s, e) =>
                {
                    e.Handled = true;
                    await LoadEntriesToListViewAsync(feed.Id);
                };
                rootNode.Items.Add(feedNode);
            }
        }

        // 指定したフィードの記事を中央ペイン（リストビュー）に読み込む
        private async Task LoadEntriesToListViewAsync(long feedId)
        {
            currentArticles.Clear();
            var articles = await _repository.GetEntriesByFeedIdAsync(feedId);
            foreach (var article in articles)
            {
                currentArticles.Add(article);
            }
            ArticleListView.ItemsSource = currentArticles;
        }

        // フィード情報をデータベースに登録する
        private async Task AddFeedToDatabaseAsync(string folderPath, string title, string url)
        {
            using var connection = new SqliteConnection(dbConnectionString);
            await connection.OpenAsync();

            // フィードデータを挿入（URL重複時は無視）
            string insertQuery = @"
                INSERT OR IGNORE INTO feeds (folder_path, title, url)
                VALUES (@folderPath, @title, @url)";

            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@folderPath", folderPath);
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@url", url);

            await command.ExecuteNonQueryAsync();
        }
 
        // 指定されたURLから記事を取得し、データベースに保存する
        private async Task FetchAndSaveEntriesAsync(long feedId, string url, SqliteConnection connection)
        {
            try
            {
                // URLからRSSフィードデータを取得
                using var response = await httpClient.GetAsync(url);
                using var stream = await response.Content.ReadAsStreamAsync();
                using var xmlReader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(xmlReader);

                // 取得した各記事データをデータベースへ挿入
                foreach (var item in feed.Items)
                {
                    // 1. 記事のURLを取得
                    string entryUrl = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

                    // 2. 投稿日時の補正（0001年問題を回避）
                    var pubDate = (item.PublishDate.Year < 1900) ? DateTimeOffset.Now : item.PublishDate;

                    // 3. 本文（SummaryかContent）の取得
                    string contentText = item.Summary?.Text ?? "";
                    if (string.IsNullOrEmpty(contentText) && item.Content is TextSyndicationContent textContent)
                    {
                        contentText = textContent.Text;
                    }

                    // 4. SQLクエリの作成（ここで insertQuery を定義するよ）
                    string insertQuery = @"INSERT OR IGNORE INTO entries (feed_id, title, url, summary, published_date)
                                         VALUES (@feedId, @title, @url, @summary, @pubDate)";

                    using var insertCmd = new SqliteCommand(insertQuery, connection);

                    // 5. パラメータのセット
                    insertCmd.Parameters.AddWithValue("@feedId", feedId);
                    insertCmd.Parameters.AddWithValue("@title", item.Title?.Text ?? "No Title");
                    insertCmd.Parameters.AddWithValue("@url", entryUrl);
                    insertCmd.Parameters.AddWithValue("@summary", contentText);
                    insertCmd.Parameters.AddWithValue("@pubDate", pubDate.ToString("yyyy/MM/dd HH:mm"));

                    await insertCmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // 取得失敗時はログ記録等の処理を想定（今回はスキップ）
            }
        }
        #endregion
    }
}