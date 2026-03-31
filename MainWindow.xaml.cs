using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.UIHelpers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

        private readonly FeedService _feedService;
        private readonly FeedDiscoveryService _discoveryService;
        private readonly TreeBuilder _treeBuilder;
        private readonly ContextMenuBuilder _menuBuilder;
        private readonly TreeDragDropHandler _dragHandler;
        private readonly FeedUpdateService _updateService;
        #endregion

        #region --- 初期設定系 ---
        public MainWindow()
        {
            InitializeComponent();

            // 画面が表示された後に実行する
            this.Loaded += (s, e) =>
            {
                TestArea.Run();
            };

            SetupWindowIcon();

            // リポジトリを初期化（ファイルパスを指定）
            _repository = new FeedRepository("feedgem.db");
            _repository.Initialize(); // 旧 InitializeDatabase() の代わり

            _feedService = new FeedService(_repository);
            _updateService = new FeedUpdateService(_repository, _feedService);
            _discoveryService = new FeedDiscoveryService();
            _treeBuilder = new TreeBuilder(_repository);

            // 起動時にデータを画面に反映させる
            _ = LoadFeedsToTreeViewAsync();

            // バックグラウンドでの更新処理を開始
            _ = _updateService.UpdateAllAsync();
            _ = StartBackgroundPollingAsync();

            _menuBuilder = new ContextMenuBuilder(
                _repository,
                _feedService,
                _updateService,
                LoadFeedsToTreeViewAsync,
                LogTextBlock
            );

            _dragHandler = new TreeDragDropHandler(
                _repository,
                LoadFeedsToTreeViewAsync
            );
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
            var candidates = await _discoveryService.DiscoverFeedsAsync(url);

            bool added = false; // 追加が行われたかを判定するフラグ

            if (candidates.Count == 1)
            {
                // 1つだけ見つかった場合はそのまま登録
                var selected = candidates[0];
                await _repository.AddFeedAsync("/", selected.Title, selected.Url);

                // 登録直後に記事をダウンロードする
                await _feedService.FetchAndSaveEntriesAsync(selected.Url);

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
                        await _feedService.FetchAndSaveEntriesAsync(selected.Url);
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

        // 右クリックメニューの動的生成と表示
        private void FeedTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);

            var menu = _menuBuilder.Build(treeViewItem);

            if (treeViewItem != null)
            {
                treeViewItem.Focus();
                treeViewItem.IsSelected = true;
                treeViewItem.ContextMenu = menu;
            }
            else
            {
                FeedTreeView.ContextMenu = menu;
            }

            menu.IsOpen = true;
            e.Handled = true;
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

        // ドラッグ開始位置の記録
        private void FeedTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragHandler.OnMouseLeftButtonDown(e);
        }

        // マウス移動時にドラッグを開始する
        private void FeedTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _dragHandler.OnMouseMove(sender, e);
        }

        // ドラッグ中のマウスカーソル状態
        private void FeedTreeView_DragOver(object sender, DragEventArgs e)
        {
            _dragHandler.OnDragOver(e);
        }

        // ドロップされた時の処理
        private async void FeedTreeView_Drop(object sender, DragEventArgs e)
        {
            await _dragHandler.OnDrop(sender, e);
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
                await _updateService.UpdateAllAsync();
            }
        }
        #endregion

        #region --- DBアクセス ---

        // フォルダ階層を考慮してフィード一覧を表示する
        private async Task LoadFeedsToTreeViewAsync()
        {
            FeedTreeView.Items.Clear();

            var nodes = await _treeBuilder.BuildTreeAsync();

            foreach (var node in nodes)
            {
                // フィード選択イベント再設定
                AttachSelectionHandler(node);

                FeedTreeView.Items.Add(node);
            }
        }

        // TreeViewItemに選択イベントを再帰的に付与する
        private void AttachSelectionHandler(TreeViewItem item)
        {
            if (item.Tag is long feedId)
            {
                item.Selected += async (s, e) =>
                {
                    e.Handled = true;
                    await LoadEntriesToListViewAsync(feedId);
                };
            }

            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                {
                    AttachSelectionHandler(childItem);
                }
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
        #endregion
    }
}