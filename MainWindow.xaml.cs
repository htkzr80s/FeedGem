using FeedGem.Data;
using FeedGem.Models;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace FeedGem
{
    public partial class MainWindow : Window
    {
        private static readonly ObservableCollection<ArticleItem> value = [];
        #region --- フィールド定義 ---

        // 記事リスト（中央ペイン）に表示するためのデータ管理用
        private readonly ObservableCollection<ArticleItem> currentArticles = value;

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
                string html = FeedHelper.GeneratePreviewHtml(selectedArticle.Title, selectedArticle.Summary);

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

        // URL入力バーの右にあるエンターボタン（⏎）をクリックした時の処理
        private void UrlEnterButton_Click(object sender, RoutedEventArgs e)
        {
            // Enterキーが押された時と同じ処理を呼び出す
            PerformUrlSubscribe();
        }

        // テキストボックス（SearchBox）でキーが押された時の処理
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Enterキーが押されたか確認
            if (e.Key == Key.Enter)
            {
                PerformUrlSubscribe(); // 購読処理を実行
            }
        }

        // 実際の購読処理をまとめたメソッド
        private async void PerformUrlSubscribe()
        {
            string url = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(url) || url == "URLを入力してEnter...") return;

            // フィードの探索
            var candidates = await DiscoverFeedsAsync(url);

            bool added = false; // 追加が行われたかを判定するフラグ

            if (candidates.Count == 1)
            {
                // 1つだけ見つかった場合はそのまま登録
                var selected = candidates[0];
                await _repository.AddFeedAsync("/", selected.Title, selected.Url);

                // 登録直後に記事をダウンロードする
                await FetchAndSaveEntriesAsync(selected.Url);

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
                        await FetchAndSaveEntriesAsync(selected.Url);
                    }
                    added = true;
                }
            }
            // 探索に失敗した場合などのケア
            else
            {
                MessageBox.Show("フィードが見つかりません。URLが正しいか確認してください。", "お知らせ");
            }

            if (added)
            {
                // 修正箇所2：購読完了したら入力バーを空にする
                SearchBox.Text = "";
                // ツリービューを更新
                await LoadFeedsToTreeViewAsync();
            }
        }

        // 記事検索ボックスにフォーカスが当たった時の処理
        private void FilterBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (FilterBox.Text == "記事を検索...")
            {
                FilterBox.Text = "";
                FilterBox.Foreground = Brushes.Black;
            }
        }

        // 記事検索ボックスの文字が変更された時の処理（絞り込み）
        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ListViewのデフォルトビューを取得してフィルタリング
            var view = CollectionViewSource.GetDefaultView(ArticleListView.ItemsSource);
            if (view == null) return;

            string keyword = FilterBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword) || keyword == "記事を検索...")
            {
                view.Filter = null; // 検索文字が空ならフィルタ解除
            }
            else
            {
                view.Filter = item =>
                {
                    if (item is ArticleItem article)
                    {
                        // タイトルにキーワードが含まれているか（大文字小文字を区別しない）
                        return article.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                };
            }
        }

        // 記事検索バーの右にあるクリアボタン（✕）をクリックした時の処理
        private void FilterClearButton_Click(object sender, RoutedEventArgs e)
        {
            FilterBox.Text = ""; // 検索バーを空にする（これで自動的にフィルタも解除される）
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

        // 新しいフォルダを作成する処理
        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            // ユーザーに入力ダイアログを出す（Microsoft.VisualBasicの参照が必要）
            string folderName = Microsoft.VisualBasic.Interaction.InputBox(
                "新しいフォルダ名を入力してください：", "フォルダ作成", "新しいフォルダ");

            if (string.IsNullOrWhiteSpace(folderName)) return;

            try
            {
                // DB上ではfolder_pathがディレクトリ構造を表しているから
                // 「ダミーのフィード」をそのパスに入れてやることで、フォルダを出現させるよ
                // URLは重複しないように、guidなどを使ってユニークにする
                string dummyUrl = $"folder://{Guid.NewGuid()}";

                // "/" 始まりでなければ補完する
                string path = folderName.StartsWith('/') ? folderName : "/" + folderName;

                // DBに登録（タイトルをフォルダ名と同じにしておけば管理しやすいね）
                await _repository.AddFeedAsync(path, $"({folderName}の管理用項目)", dummyUrl);

                LogTextBlock.Text = $"フォルダ「{folderName}」を作成しました。";

                // ツリーを再構築して、新しく作ったフォルダを表示させる
                await LoadFeedsToTreeViewAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダ作成中にエラーが発生しました。\n{ex.Message}");
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
                        if (!newFolder.StartsWith('/')) newFolder = "/" + newFolder;

                        await _repository.UpdateFeedAsync(feedId, newFolder, target.Title, target.Url);
                        await LoadFeedsToTreeViewAsync(); // ツリーを再描画
                    }
                }
            }
        }

        // 右クリック時に、マウスの下にあるTreeViewItemを自動的に選択状態にする処理
        private void FeedTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject depObj)
            {
                // クリックされたUI要素から親を辿ってTreeViewItemを探す
                bool v = depObj is TreeViewItem;
                while (depObj != null && !v)
                {
                    depObj = VisualTreeHelper.GetParent(depObj);
                }

                if (depObj is TreeViewItem item)
                {
                    item.Focus();
                    item.IsSelected = true;
                }
            }
        }

        // OPMLファイルを読み込んでフィードを一括登録する
        private async void ImportOpml_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "OPMLファイル (*.opml;*.xml)|*.opml;*.xml" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var doc = XDocument.Load(dialog.FileName);
                var body = doc.Root?.Element("body");
                if (body == null) return;

                int count = 0;
                // 再帰的にoutlineタグを解析してフォルダ構造を維持する
                await ProcessOutlineElements(body.Elements("outline"), "/");

                async Task ProcessOutlineElements(IEnumerable<XElement> elements, string currentPath)
                {
                    foreach (var outline in elements)
                    {
                        string title = outline.Attribute("text")?.Value ?? outline.Attribute("title")?.Value ?? "無題";
                        string xmlUrl = outline.Attribute("xmlUrl")?.Value ?? "";

                        if (!string.IsNullOrEmpty(xmlUrl))
                        {
                            // フィードURLがある場合は登録
                            await _repository.AddFeedAsync(currentPath, title, xmlUrl);
                            count++;
                        }
                        else if (outline.Elements("outline").Any())
                        {
                            // 子のoutlineがある場合はフォルダとして扱い、中身を解析
                            string nextPath = currentPath == "/" ? $"/{title}" : $"{currentPath}/{title}";
                            await ProcessOutlineElements(outline.Elements("outline"), nextPath);
                        }
                    }
                }

                LogTextBlock.Text = $"{count}件のフィードをインポートしました。";
                await LoadFeedsToTreeViewAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"インポート失敗: {ex.Message}");
            }
        }

        // 現在のフィード一覧をOPML形式で書き出す
        private async void ExportOpml_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "OPMLファイル (*.opml)|*.opml", FileName = "feeds.opml" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var feeds = await _repository.GetAllFeedsAsync();

                // 階層構造を作るための下準備
                var rootElement = new XElement("opml", new XAttribute("version", "2.0"),
                                    new XElement("head", new XElement("title", "FeedGem Export")),
                                    new XElement("body"));

                var body = rootElement.Element("body");
                if (body == null) return;

                // フォルダごとにグループ化して出力
                var folders = feeds.GroupBy(f => f.FolderPath);
                foreach (var folder in folders)
                {
                    XContainer targetContainer = body;

                    // ルート以外ならフォルダ用のoutlineタグを作る
                    if (folder.Key != "/")
                    {
                        var folderNode = new XElement("outline", new XAttribute("text", folder.Key.TrimStart('/')));
                        body.Add(folderNode);
                        targetContainer = folderNode;
                    }

                    foreach (var f in folder)
                    {
                        targetContainer.Add(new XElement("outline",
                            new XAttribute("text", f.Title),
                            new XAttribute("title", f.Title),
                            new XAttribute("type", "rss"),
                            new XAttribute("xmlUrl", f.Url)));
                    }
                }

                new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rootElement).Save(dialog.FileName);
                LogTextBlock.Text = "エクスポートが完了しました。";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エクスポート失敗: {ex.Message}");
            }
        }
        #endregion

        #region --- UI生成ヘルパー ---

        // フォルダ・フィード用のヘッダーUI（アイコン付き）を生成する
        private static StackPanel CreateTreeItemHeader(string text, bool isFolder, string? url = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (isFolder)
            {
                var folderIcon = new TextBlock
                {
                    Text = "📁", // フォルダアイコン
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(folderIcon);
            }
            else
            {
                var image = new System.Windows.Controls.Image
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                try
                {
                    // GoogleのFavicon APIを利用してアイコンを取得
                    if (!string.IsNullOrEmpty(url))
                    {
                        var uri = new Uri(url);
                        string faviconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=16";
                        image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(faviconUrl));
                    }
                }
                catch { /* URL解析失敗時などはアイコンなしで続行 */ }

                panel.Children.Add(image);
            }

            var textBlock = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(textBlock);

            return panel;
        }
        #endregion

        #region --- ドラッグ＆ドロップ関連 ---

        private Point _startPoint;
        private TreeViewItem? _dragSourceItem;

        // ドラッグ開始位置の記録
        private void FeedTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        // マウス移動時にドラッグを開始する
        private void FeedTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _startPoint - mousePos;

                // 一定距離マウスが動いたらドラッグと判定
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                    if (treeViewItem != null)
                    {
                        _dragSourceItem = treeViewItem;
                        DragDrop.DoDragDrop(treeViewItem, treeViewItem, DragDropEffects.Move);
                    }
                }
            }
        }

        // ドラッグ中のマウスカーソル状態
        private void FeedTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        // ドロップされた時の処理
        private async void FeedTreeView_Drop(object sender, DragEventArgs e)
        {
            if (_dragSourceItem == null) return;

            var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == _dragSourceItem) return;

            // 今回はフィードの移動のみサポート（フォルダ自体の移動は中身のケアが必要なので弾く）
            if (_dragSourceItem.Tag is not long feedId)
            {
                MessageBox.Show("フォルダの移動は今回対象外だよ。");
                return;
            }

            string newFolderPath = "/"; // デフォルトはルート
            if (targetItem != null)
            {
                if (targetItem.Tag is string folderPath)
                {
                    // ターゲットがフォルダノード
                    newFolderPath = folderPath;
                }
                else if (targetItem.Tag is long)
                {
                    // ターゲットが別のフィードノードなら、親のフォルダを取得
                    var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(targetItem));
                    if (parentItem != null && parentItem.Tag is string pPath)
                    {
                        newFolderPath = pPath;
                    }
                }
            }

            // DB上のフォルダパスを更新
            var feeds = await _repository.GetAllFeedsAsync();
            var target = feeds.FirstOrDefault(f => f.Id == feedId);
            if (target != null && target.FolderPath != newFolderPath)
            {
                await _repository.UpdateFeedAsync(feedId, newFolderPath, target.Title, target.Url);
                await LoadFeedsToTreeViewAsync(); // ツリーを再描画
            }
        }

        // 指定した型の親要素をビジュアルツリーから探し出す
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            } while (current != null);
            return null;
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
                        // 修正箇所1：タイトルが空の場合の対策
                        string title = item.Title?.Text ?? "無題";
                        string url = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";
                        string summary = item.Summary?.Text ?? "";

                        // 修正箇所2：日付データが欠落している際のエラー対策
                        DateTimeOffset pubDate = item.PublishDate != default ? item.PublishDate :
                                               item.LastUpdatedTime != default ? item.LastUpdatedTime :
                                               DateTimeOffset.Now;

                        // リポジトリ経由でデータベースに保存
                        await _repository.SaveEntryAsync(feed.Id, title, url, summary, pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm"));
                    }
                }
                catch (Exception ex)
                {
                    // 通信エラーなどはデバッグ出力に記録して次へ進む
                    Debug.WriteLine($"フィード更新失敗: {feed.Title} - {ex.Message}");
                }
            }

            // 古い記事の自動削除を実行
            await _repository.DeleteOldEntriesAsync();

            // 画面上の最終更新時刻を更新
            await Dispatcher.InvokeAsync(() =>
            {
                LastUpdateTextBlock.Text = $"最終更新: {DateTime.Now:HH:mm:ss}";
            });

            // 念のため、現在表示中の記事リストもリフレッシュする
            if (FeedTreeView.SelectedItem is TreeViewItem selectedNode && selectedNode.Tag is long feedId)
            {
                await LoadEntriesToListViewAsync(feedId);
            }
        }

        // フィード候補を探す処理（実態は FeedHelper に移譲）
        private static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string targetUrl) => await FeedHelper.DiscoverFeedsAsync(targetUrl);
        #endregion

        #region --- DBアクセス ---

        // フォルダ階層を考慮してフィード一覧を表示する
        private async Task LoadFeedsToTreeViewAsync()
        {
            FeedTreeView.Items.Clear();
            var feeds = await _repository.GetAllFeedsAsync();

            var folderNodes = new Dictionary<string, TreeViewItem>();

            foreach (var feed in feeds)
            {
                var pathParts = feed.FolderPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
                ItemsControl parent = FeedTreeView;
                string currentKey = "";

                foreach (var part in pathParts)
                {
                    currentKey += "/" + part;
                    if (!folderNodes.TryGetValue(currentKey, out TreeViewItem? value))
                    {
                        // 変更：リッチなUIを設定し、Tagにフォルダのパス文字列を保持させる
                        var newNode = new TreeViewItem
                        {
                            Header = CreateTreeItemHeader(part, true),
                            IsExpanded = true,
                            Tag = currentKey
                        };
                        parent.Items.Add(newNode);
                        value = newNode;
                        folderNodes[currentKey] = value;
                    }
                    parent = value;
                }

                int unreadCount = await _repository.GetUnreadCountAsync(feed.Id);
                string displayText = unreadCount > 0 ? $"{feed.Title} ({unreadCount})" : feed.Title;

                // 変更：Favicon対応のUIを設定
                var feedNode = new TreeViewItem
                {
                    Header = CreateTreeItemHeader(displayText, false, feed.Url),
                    Tag = feed.Id // 記事の場合はlong型のIDを保持
                };
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
                // 記事の取得が終わったら、古い記事を掃除する
                await _repository.DeleteOldEntriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"記事取得失敗: {ex.Message}");
            }
        }
        #endregion
    }
}