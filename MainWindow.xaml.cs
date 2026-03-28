using FeedGem.Data;
using FeedGem.Models;
using HtmlAgilityPack;
using Microsoft.VisualBasic;
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

namespace FeedGem
{
    public partial class MainWindow : Window
    {
        #region --- フィールド定義 ---

        // HTTP通信用のクライアントインスタンスを生成
        // アプリケーション全体で再利用してリソースの枯渇を防ぐ
        private static readonly HttpClient httpClient = new();

        // 記事リスト（中央ペイン）に表示するためのデータ管理用
        private ObservableCollection<ArticleItem> currentArticles = [];

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
        #endregion

        #region --- UIイベントハンドラ ---

        // 記事リストの選択が変更された際の処理
        private async void ArticleListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArticleListView.SelectedItem is ArticleItem selectedArticle)
            {
                // 既読処理
                if (!selectedArticle.IsRead)
                {
                    selectedArticle.IsRead = true;
                    await _repository.MarkAsReadAsync(selectedArticle.Url);
                }

                // WebView2の準備ができているか確認
                await PreviewBrowser.EnsureCoreWebView2Async(null);

                // ヘルパークラスを使ってHTMLを生成する
                string html = FeedGem.Helpers.FeedHelper.GeneratePreviewHtml(selectedArticle.Title, selectedArticle.Summary);

                // プレビュー表示
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
                if (string.IsNullOrEmpty(url)) return;

                // フィードの探索（既存のロジック）
                var candidates = await DiscoverFeedsAsync(url);

                bool added = false; // 追加が行われたかを判定するフラグ

                if (candidates.Count == 1)
                {
                    // 1つだけ見つかった場合はそのまま登録
                    var selected = candidates[0];
                    await _repository.AddFeedAsync("/", selected.Title, selected.Url);

                    // 登録直後に記事をダウンロードする
                    await FetchAndSaveEntriesAsync(selected.Url);

                    // ツリービューを更新
                    await LoadFeedsToTreeViewAsync();
                    added = true;
                }
                else if (candidates.Count > 1)
                {
                    // 複数見つかった場合は選択ウィンドウを表示
                    var selectionWindow = new FeedSelectionWindow(candidates) { Owner = this };

                    if (selectionWindow.ShowDialog() == true)
                    {
                        foreach (var selected in selectionWindow.SelectedFeeds)
                        {
                            await _repository.AddFeedAsync("/", selected.Title, selected.Url);
                            // 選択されたものそれぞれをダウンロード
                            await FetchAndSaveEntriesAsync(selected.Url);
                        }
                        await LoadFeedsToTreeViewAsync();
                        added = true;
                    }

                    if (added)
                    {
                        SearchBox.Text = ""; // 検索バーを空にする
                        await LoadFeedsToTreeViewAsync();
                    }
                }
            }
        }

        // フィード削除処理
        private async void DeleteFeed_Click(object sender, RoutedEventArgs e)
        {
            if (FeedTreeView.SelectedItem is TreeViewItem selectedNode)
            {
                // Tagが空（null）＝フォルダ
                if (selectedNode.Tag == null)
                {
                    MessageBox.Show("フォルダの削除・編集機能は、中身への影響が大きいため今回は対象外だよ。");
                    return;
                }

                long feedId = (long)selectedNode.Tag;
                var result = MessageBox.Show("このサイトと記事をすべて削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    await _repository.DeleteFeedAsync(feedId);
                    currentArticles.Clear(); // 中央のリストをクリアする
                    PreviewBrowser.Source = new Uri("about:blank"); // 右ペインをクリア
                    await LoadFeedsToTreeViewAsync();
                }
            }
        }

        // フィードの名前（タイトル）を変更する処理
        private async void RenameFeed_Click(object sender, RoutedEventArgs e)
        {
            // ツリービューで選択されている項目を取得
            if (FeedTreeView.SelectedItem is TreeViewItem selectedNode)
            {
                // Tagが空（null）＝フォルダ
                if (selectedNode.Tag == null)
                {
                    MessageBox.Show("フォルダの削除・編集機能は、中身への影響が大きいため今回は対象外だよ。");
                    return;
                }
                // 現在の表示名から (未読数) を除いた純粋なタイトルを取得
                string currentTitle = selectedNode.Header.ToString()!.Split(" (")[0];

                // 入力ダイアログを表示
                string newTitle = Interaction.InputBox("新しい名前を入力してください", "名前の変更", currentTitle);

                long feedId = (long)selectedNode.Tag;

                if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != currentTitle)
                {
                    // DBの情報を更新（フォルダやURLはそのまま）
                    var feeds = await _repository.GetAllFeedsAsync();
                    var target = feeds.FirstOrDefault(f => f.Id == feedId);

                    if (target != null)
                    {
                        await _repository.UpdateFeedAsync(feedId, target.FolderPath, newTitle, target.Url);
                        await LoadFeedsToTreeViewAsync(); // ツリーを再描画
                    }
                }
            }
        }

        // フィードの所属フォルダを変更する処理
        private async void MoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FeedTreeView.SelectedItem is TreeViewItem selectedNode && selectedNode.Tag is long feedId)
            {
                var feeds = await _repository.GetAllFeedsAsync();
                var target = feeds.FirstOrDefault(f => f.Id == feedId);

                if (target != null)
                {
                    // 現在のフォルダパスを入力初期値にする
                    string newFolder = Interaction.InputBox("移動先のフォルダパスを入力してください\n例: /News/IT", "フォルダの変更", target.FolderPath);

                    if (!string.IsNullOrWhiteSpace(newFolder))
                    {
                        // 先頭が / で始まっていない場合は補完する
                        if (!newFolder.StartsWith("/")) newFolder = "/" + newFolder;

                        await _repository.UpdateFeedAsync(feedId, newFolder, target.Title, target.Url);
                        await LoadFeedsToTreeViewAsync(); // ツリーを再描画
                    }
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

        // URLからフィード（RSS/Atom）の候補を探すメソッド
        private async Task<List<FeedCandidate>> DiscoverFeedsAsync(string targetUrl)
        {
            var candidates = new List<FeedCandidate>();

            // 1. まず入力されたURLそのものをフィードとして試す（SourceForge直入力などのケース）
            try
            {
                using var reader = XmlReader.Create(targetUrl);
                var feed = SyndicationFeed.Load(reader);
                candidates.Add(new FeedCandidate { Title = feed.Title.Text, Url = targetUrl });
                return candidates;
            }
            catch { /* 次のHTML解析へ */ }

            // 2. HTML内からフィードURLを探す
            try
            {
                var web = new HtmlWeb();
                var doc = await web.LoadFromWebAsync(targetUrl);

                // RSS/Atomを示唆するlinkタグを広めに探す
                var nodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate' or @type='application/rss+xml' or @type='application/atom+xml']");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string href = node.GetAttributeValue("href", "");
                        string type = node.GetAttributeValue("type", "").ToLower();
                        string title = node.GetAttributeValue("title", "");

                        if (string.IsNullOrEmpty(href)) continue;

                        // 記事そのもののリンクや、コメント用フィードなどを除外するフィルタ
                        if (href.Contains("comment") || href.Contains("trackback")) continue;

                        // 相対パス（/feed等）を絶対パス（https://.../feed）に変換
                        Uri baseUri = new Uri(targetUrl);
                        Uri fullUri = new Uri(baseUri, href);
                        string absoluteUrl = fullUri.AbsoluteUri;

                        // タイトルが空ならサイトの<title>を借りる
                        if (string.IsNullOrWhiteSpace(title) || title.ToUpper() == "RSS")
                        {
                            title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "不明なフィード";
                        }

                        // 重複チェックをして追加
                        if (!candidates.Any(c => c.Url == absoluteUrl))
                        {
                            candidates.Add(new FeedCandidate { Title = title, Url = absoluteUrl });
                        }
                    }
                }
            }
            catch { /* 次のHTML解析へ */ }

            // 3. よくあるフィードURLを推測して試す
            var commonPaths = new[]
            {
                "/feed",
                "/rss",
                "/rss.xml",
                "/atom.xml",
                "/index.xml",
                "/feeds/posts/default" // FC2やBlogger系
            };

            foreach (var path in commonPaths)
            {
                try
                {
                    Uri baseUri = new Uri(targetUrl);
                    Uri testUri = new Uri(baseUri, path);

                    using var reader = XmlReader.Create(testUri.AbsoluteUri);
                    var feed = SyndicationFeed.Load(reader);

                    if (feed != null && !candidates.Any(c => c.Url == testUri.AbsoluteUri))
                    {
                        candidates.Add(new FeedCandidate
                        {
                            Title = feed.Title?.Text ?? "フィード",
                            Url = testUri.AbsoluteUri
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"探索エラー: {ex.Message}");
                }
            }
            return candidates;
        }
        #endregion

        #region --- DBアクセス ---

        // フォルダ階層を考慮してフィード一覧を表示する
        private async Task LoadFeedsToTreeViewAsync()
        {
            FeedTreeView.Items.Clear();
            var feeds = await _repository.GetAllFeedsAsync();

            // フォルダパスをキーにして、作成済みのツリーノードを管理する辞書
            var folderNodes = new Dictionary<string, TreeViewItem>();

            foreach (var feed in feeds)
            {
                // フォルダパス（例: /News/Tech）を「/」で区切って階層を作る
                var pathParts = feed.FolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                ItemsControl parent = FeedTreeView;
                string currentKey = "";

                foreach (var part in pathParts)
                {
                    currentKey += "/" + part;
                    if (!folderNodes.ContainsKey(currentKey))
                    {
                        var newNode = new TreeViewItem { Header = part, IsExpanded = true };
                        parent.Items.Add(newNode);
                        folderNodes[currentKey] = newNode;
                    }
                    parent = folderNodes[currentKey];
                }

                // 実際のフィードノードを作成
                int unreadCount = await _repository.GetUnreadCountAsync(feed.Id);
                string displayText = unreadCount > 0 ? $"{feed.Title} ({unreadCount})" : feed.Title;

                var feedNode = new TreeViewItem { Header = displayText, Tag = feed.Id }; // TagにIDを隠しておく
                feedNode.Selected += async (s, e) => {
                    e.Handled = true;
                    await LoadEntriesToListViewAsync(feed.Id);
                };

                parent.Items.Add(feedNode);
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

        // 指定されたURLから記事を取得し、データベースに保存する
        private async Task FetchAndSaveEntriesAsync(string url)
        {
            // DBから登録情報の詳細（IDなど）を逆引きする
            var feeds = await _repository.GetAllFeedsAsync();
            var target = feeds.FirstOrDefault(f => f.Url == url);
            if (target == null) return;

            try
            {
                using var reader = XmlReader.Create(url);
                var rssData = SyndicationFeed.Load(reader);

                foreach (var item in rssData.Items)
                {
                    string title = item.Title?.Text ?? "無題";
                    string link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";
                    string summary = item.Summary?.Text ?? "";
                    if (string.IsNullOrEmpty(summary) && item.Content is TextSyndicationContent textContent)
                    {
                        summary = textContent.Text;
                    }
                    summary ??= ""; // それでもnullなら空文字に

                    // 安全な日付取得
                    DateTimeOffset pubDate = item.PublishDate != default ? item.PublishDate :
                                           item.LastUpdatedTime != default ? item.LastUpdatedTime :
                                           DateTimeOffset.Now;

                    // リポジトリに保存を依頼
                    await _repository.SaveEntryAsync(target.Id, title, link, summary, pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"記事取得失敗: {ex.Message}");
            }
        }
        #endregion
    }
}