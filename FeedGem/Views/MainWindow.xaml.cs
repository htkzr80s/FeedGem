using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.UIHelpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Input = System.Windows.Input;
using Media = System.Windows.Media;
using MsgBox = System.Windows.MessageBox;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace FeedGem.Views
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ArticleItem> currentArticles = [];

        // データベース操作を専門に行うインスタンス
        private readonly FeedRepository _repository;

        private readonly FeedService _feedService;
        private readonly FeedDiscoveryService _discoveryService;
        private readonly TreeBuilder _treeBuilder;
        private readonly ContextMenuBuilder _menuBuilder;
        private readonly TreeDragDropHandler _dragHandler;
        private readonly FeedUpdateService _updateService;
        private readonly OpmlService _opmlService;
        private readonly UrlSubscriptionService _subscriptionService;
        private readonly BackgroundUpdateTimer _backgroundTimer;
        private long? _currentSelectedFeedId;
        // --- トレイ関連 ---
        private readonly UnreadCountService _unreadService;
        private readonly NotificationService _notificationService;

        public MainWindow()
        {
            InitializeComponent();

            // Configからウィンドウ設定を読み込み、位置・サイズ・カラム幅を復元
            // 初回はデフォルト値（Config.ini自動生成）
            var config = App.LoadConfig();  // App経由で一元化
            this.Left = config.WindowLeft;
            this.Top = config.WindowTop;
            this.Width = config.WindowWidth;
            this.Height = config.WindowHeight;
            colFeedTree.Width = new GridLength(config.FeedTreeWidth);
            colArticleList.Width = new GridLength(config.ArticleListWidth);

            // XAMLでx:Nameを付けたColumnDefinitionに幅を復元
            colFeedTree?.Width = new GridLength(config.FeedTreeWidth);
            colArticleList?.Width = new GridLength(config.ArticleListWidth);

            // マルチモニタ対策：ウィンドウが表示されない位置ならプライマリ中央へ
            App.EnsureWindowOnScreen(this);

            SetupWindowIcon();

            ArticleListView.ItemsSource = currentArticles;

            // リポジトリを初期化（ファイルパスを指定）
            _repository = new FeedRepository("feedgem.db");
            _repository.Initialize();
            _unreadService = new UnreadCountService(_repository);
            _notificationService = new NotificationService(RestoreWindow);
            _feedService = new FeedService(_repository);
            _updateService = new FeedUpdateService(_repository, _feedService);
            _discoveryService = new FeedDiscoveryService();
            _treeBuilder = new TreeBuilder(_repository);
            _opmlService = new OpmlService(_repository);
            _subscriptionService = new UrlSubscriptionService(_repository, _feedService);

            _menuBuilder = new ContextMenuBuilder(
                _repository,
                _feedService,
                _updateService,
                LoadFeedsToTreeViewAsync,
                LogTextBlock,
                UpdateLastUpdateTime,
                ImportOpmlAsync,
                ExportOpmlAsync,
                RefreshCurrentArticleListAsync
            );

            // 起動時にデータを画面に反映させる
            _ = LoadFeedsToTreeViewAsync();

            // バックグラウンドでの更新処理を開始
            _ = _updateService.UpdateAllAsync();
            _backgroundTimer = new BackgroundUpdateTimer(
                _updateService,
                UpdateTrayIconAsync,
                UpdateLastUpdateTime,
                this.Dispatcher
            );

            // 1時間ごとに実行
            _backgroundTimer.Start(TimeSpan.FromHours(1));

            _dragHandler = new TreeDragDropHandler(
                _repository,
                LoadFeedsToTreeViewAsync
            );

            this.StateChanged += MainWindow_StateChanged;

            _ = UpdateTrayIconAsync();
            UpdateLastUpdateTime();

            // テーマ変更イベントを購読
            ThemeManager.ThemeChanged += OnThemeChanged;

            // WebView2の初期化処理を非同期で開始
            _ = InitializeWebViewAsync();
        }

        // WebView2の初期化と、新規ページ読み込み時のスタイル自動適用を行う
        private async Task InitializeWebViewAsync()
        {
            await PreviewBrowser.EnsureCoreWebView2Async(null);

            var config = App.LoadConfig();
            string theme = config.Theme == "Auto"
                ? ThemeManager.GetSystemTheme()
                : config.Theme;

            PreviewBrowser.DefaultBackgroundColor =
                theme == "Dark"
                ? System.Drawing.Color.FromArgb(255, 32, 32, 32)
                : System.Drawing.Color.White;

            // 初回CSS（軽量版）
            await WebViewThemeService.InitializeAsync(PreviewBrowser.CoreWebView2, theme);

            // ナビゲーション後に毎回再適用
            PreviewBrowser.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                var config = App.LoadConfig();
                string theme = config.Theme == "Auto"
                    ? ThemeManager.GetSystemTheme()
                    : config.Theme;

                PreviewBrowser.DefaultBackgroundColor =
                    theme == "Dark"
                    ? System.Drawing.Color.FromArgb(255, 32, 32, 32)
                    : System.Drawing.Color.White;

                await WebViewThemeService.ApplyAsync(PreviewBrowser.CoreWebView2, theme);
            };

            // 初回表示にも適用
            await WebViewThemeService.ApplyAsync(PreviewBrowser.CoreWebView2, theme);
        }

        // テーマ変更時に呼ばれ、WebView2のプロファイル設定と現在表示中のページ色を同期する
        private async void OnThemeChanged(string themeName)
        {
            if (PreviewBrowser.CoreWebView2 != null)
            {
                PreviewBrowser.CoreWebView2.Profile.PreferredColorScheme =
                    themeName == "Dark"
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;

                await WebViewThemeService.ApplyAsync(PreviewBrowser.CoreWebView2, themeName);
            }
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

        // 最小化時にトレイへ
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide(); // タスクバーから消える
            }
        }

        // ウィンドウ復帰
        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentConfig();

            _notificationService?.Dispose();
            _backgroundTimer?.Dispose();
        }

        // 現在のウィンドウ状態（位置・サイズ・カラム幅）をConfig.iniに保存
        private void SaveCurrentConfig()
        {
            var config = App.LoadConfig();  // App経由で一元化

            // ウィンドウ状態に応じて適切な値を取得
            Rect bounds;

            // 通常状態ならそのまま
            if (this.WindowState == WindowState.Normal)
            {
                bounds = new Rect(this.Left, this.Top, this.Width, this.Height);
            }
            else
            {
                // 最大化・最小化時は復元時のサイズを使う
                bounds = this.RestoreBounds;
            }

            // 画面外に行ってる場合のガード
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                config.WindowLeft = bounds.Left;
                config.WindowTop = bounds.Top;
                config.WindowWidth = bounds.Width;
                config.WindowHeight = bounds.Height;
            }

            // カラム幅
            if (colFeedTree != null)
                config.FeedTreeWidth = colFeedTree.Width.Value;

            if (colArticleList != null)
                config.ArticleListWidth = colArticleList.Width.Value;

            App.SaveConfig(config);  // App経由でConfigManagerに投げる
        }

        // 未読アイコン更新
        private async Task UpdateTrayIconAsync()
        {
            if (_notificationService == null) return;

            int totalUnread = await _unreadService.GetTotalUnreadAsync();
            _notificationService.UpdateUnreadState(totalUnread);
        }


        // 記事リストの選択が変更された際の処理
        private async void ArticleListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArticleListView.SelectedItem is ArticleItem selectedArticle)
            {
                // 既読処理
                if (!selectedArticle.IsRead)
                {
                    await _feedService.MarkArticleAsReadAsync(selectedArticle);
                    await UpdateTrayIconAsync();
                    // ツリーを再構築して未読数を即反映
                    await UpdateUnreadCountsAsync();
                }

                // ヘルパークラスを使ってHTMLを生成する
                string html = ArticleHtmlService.BuildPreviewHtml(selectedArticle.Title, selectedArticle.Summary);

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
                SearchBox.Foreground = (Media.Brush)FindResource("TextBrush");
            }
        }

        // テキストボックス（SearchBox）でキーが押された時の処理
        private async void SearchBox_KeyDown(object sender, Input.KeyEventArgs e)
        {
            // Enterキーが押されたか確認
            if (e.Key == Key.Enter)
            {
                await PerformUrlSubscribeAsync(); // 非同期で購読処理を実行
            }
        }

        // URL入力バーの右にあるエンターボタン（⏎）をクリックした時の処理
        private async void UrlEnterButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformUrlSubscribeAsync();
        }

        // 購読処理
        private async Task PerformUrlSubscribeAsync()
        {
            string url = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(url) || url == "URLを入力してEnter...") return;

            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                LogTextBlock.Text = "フィードを探索中...";

                var candidates = await FeedDiscoveryService.DiscoverFeedsAsync(url);

                Mouse.OverrideCursor = null;

                var result = await _subscriptionService.HandleCandidatesAsync(candidates);

                switch (result)
                {

                    case SubscribeResult.Success:
                        LogTextBlock.Text = "フィードを追加しました。";
                        await LoadFeedsToTreeViewAsync();
                        break;

                    case SubscribeResult.NeedsSelection:
                        var window = new FeedSelectionWindow(candidates) { Owner = this };

                        if (window.ShowDialog() == true)
                        {
                            var selected = window.SelectedFeeds.FirstOrDefault();
                            if (selected != null)
                            {
                                var secondResult = await _subscriptionService.AddFeedAsync(selected);

                                if (secondResult == SubscribeResult.Success)
                                {
                                    LogTextBlock.Text = "フィードを追加しました。";
                                    await LoadFeedsToTreeViewAsync();
                                }
                            }
                        }
                        else
                        {
                            LogTextBlock.Text = "購読を中止しました。";
                        }
                        break;

                    case SubscribeResult.NoCandidates:
                        LogTextBlock.Text = "フィードが見つかりませんでした。";
                        MsgBox.Show("フィードが見つかりませんでした。");
                        break;

                    case SubscribeResult.TooManyCandidates:
                        LogTextBlock.Text = "購読URLが多すぎるため処理を中止しました。";
                        MsgBox.Show("購読URLが多すぎるため処理を中止しました。");
                        break;

                    case SubscribeResult.SkippedOrEmpty:
                        LogTextBlock.Text = "重複、または記事がないため購読を中止しました。";
                        MsgBox.Show("重複、または記事がないため購読を中止しました。");
                        break;
                }
            }
            finally
            {
                SearchBox.Text = "URLを入力してEnter...";
                Mouse.OverrideCursor = null;
            }
        }

        // 記事検索ボックスにフォーカスが当たった時の処理
        private void FilterBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (FilterBox.Text == "記事を検索...")
            {
                FilterBox.Text = "";
                FilterBox.Foreground = (Media.Brush)FindResource("TextBrush");
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
            FilterBox.Text = "";
            FilterBox.Text = "記事を検索...";
        }

        // 設定ボタンクリック
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow { Owner = this };
            window.ShowDialog();
        }

        // 右クリックメニューの動的生成と表示
        private void FeedTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);

            if (item != null)
            {
                // 順番重要
                item.IsSelected = true;
                item.Focus();
            }
            else
            {
                // 空白なら選択解除
                foreach (var obj in FeedTreeView.Items)
                {
                    if (obj is TreeViewItem tvi)
                        UnselectAll(tvi);
                }
            }

            var menu = _menuBuilder.Build(item);

            if (item != null)
            {
                item.ContextMenu = menu;
            }
            else
            {
                FeedTreeView.ContextMenu = menu;
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        // OPMLインポート
        private async Task ImportOpmlAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "OPMLファイル (*.opml;*.xml)|*.opml;*.xml" };
            if (dialog.ShowDialog() != true) return;
            LogTextBlock.Text = "インポート中...";

            try
            {
                int count = await _opmlService.ImportAsync(dialog.FileName);
                LogTextBlock.Text = $"{count}件のフィードをインポートしました。";
                await LoadFeedsToTreeViewAsync();
            }
            catch (Exception ex)
            {
                LoggingService.Error("OPMLインポート失敗", ex);
                MsgBox.Show($"インポート失敗: {ex.Message}");
            }
        }

        // OPMLエクスポート
        private async Task ExportOpmlAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "OPMLファイル (*.opml)|*.opml", FileName = "feeds.opml" };
            if (dialog.ShowDialog() != true) return;
            LogTextBlock.Text = "エクスポート中...";

            try
            {
                var doc = await _opmlService.ExportAsync();
                doc.Save(dialog.FileName);
                LogTextBlock.Text = "エクスポートが完了しました。";
            }
            catch (Exception ex)
            {
                LogTextBlock.Text = "エクスポートに失敗しました。";
                LoggingService.Error("OPMLエクスポート失敗", ex);
                MsgBox.Show($"エクスポート失敗: {ex.Message}");
            }
        }

        // 記事タイトルをクリップボードにコピーする処理
        private void CopyTitle_Click(object sender, RoutedEventArgs e)
        {
            // 現在選択されている行のデータを確認
            if (ArticleListView.SelectedItem is ArticleItem item)
            {
                // タイトルをクリップボードへ送る
                System.Windows.Clipboard.SetText(item.Title);
                // ユーザーに状況を伝える
                LogTextBlock.Text = "タイトルをコピーしました。";
            }
        }

        // URLをクリップボードにコピーする処理
        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            // 現在選択されている行のデータを確認
            if (ArticleListView.SelectedItem is ArticleItem item)
            {
                // URLをクリップボードへ送る
                System.Windows.Clipboard.SetText(item.Url);
                // ユーザーに状況を伝える
                LogTextBlock.Text = "URLをコピーしました。";
            }
        }

        // 表示内容をすべてクリアする（削除時などに使用）
        public void ClearAllPanels()
        {
            ArticleListView.ItemsSource = null;

            // CoreWebView2の初期化状態を確認してから操作する
            if (PreviewBrowser != null && PreviewBrowser.CoreWebView2 != null)
            {
                PreviewBrowser.NavigateToString("<html><body></body></html>");
            }
        }

        // 最終更新日時表示を更新
        private void UpdateLastUpdateTime()
        {
            LastUpdateTextBlock.Text = $"最終更新: {DateFormatService.Instance.FormatDate(DateTime.Now)}";
        }

        // TreeViewの選択を再帰的に解除
        private static void UnselectAll(TreeViewItem item)
        {
            item.IsSelected = false;

            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                    UnselectAll(childItem);
            }
        }

        // ドラッグ開始位置の記録
        private void FeedTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragHandler.OnMouseLeftButtonDown(e);
        }

        // マウス移動時にドラッグを開始する
        private void FeedTreeView_PreviewMouseMove(object sender, Input.MouseEventArgs e)
        {
            _dragHandler.OnMouseMove(sender, e);
        }

        // ドラッグ中のマウスカーソル状態
        private void FeedTreeView_DragOver(object sender, WpfDragEventArgs e)
        {
            TreeDragDropHandler.OnDragOver(e);
        }

        // ドロップされた時の処理
        private async void FeedTreeView_Drop(object sender, WpfDragEventArgs e)
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

        // フォルダ階層を考慮してフィード一覧を表示する
        private async Task LoadFeedsToTreeViewAsync()
        {
            FeedTreeView.Items.Clear();

            var nodes = await _treeBuilder.BuildTreeDataAsync();

            foreach (var node in nodes)
            {
                var item = FeedTreeItem.Create(node);
                // フィード選択イベント再設定
                AttachSelectionHandler(item);
                FeedTreeView.Items.Add(item);
            }

            // トレイアイコン更新
            await UpdateTrayIconAsync();
        }

        // TreeViewItemに選択イベントを再帰的に付与する
        private void AttachSelectionHandler(TreeViewItem item)
        {
            if (item.Tag is TreeTag tag && tag.FeedId != null)
            {
                long feedId = tag.FeedId.Value;

                item.Selected += async (s, e) =>
                {
                    e.Handled = true;
                    _currentSelectedFeedId = feedId;
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
        }

        private async Task RefreshCurrentArticleListAsync()
        {
            if (_currentSelectedFeedId.HasValue)
            {
                await LoadEntriesToListViewAsync(_currentSelectedFeedId.Value);
            }
        }

        // TreeView内の未読数だけ更新する
        private async Task UpdateUnreadCountsAsync()
        {
            foreach (TreeViewItem item in FeedTreeView.Items)
            {
                await UpdateUnreadCountsRecursive(item);
            }
        }

        // 未読数更新（差分のみ反映）
        private async Task UpdateUnreadCountsRecursive(TreeViewItem item)
        {
            if (item.Tag is TreeTag tag && tag.Type == TreeNodeType.Feed && tag.FeedId != null)
            {
                int unread = await _repository.GetUnreadCountAsync(tag.FeedId.Value);

                // 差分があるときだけ更新
                if (tag.UnreadCount != unread)
                {
                    tag.UnreadCount = unread;

                    string displayName = unread > 0
                        ? $"{tag.Name} ({unread})"
                        : tag.Name;

                    item.Header = FeedTreeHeader.Create(
                        displayName,
                        tag.Type == TreeNodeType.Folder,
                        tag.Url
                    );
                }
            }

            foreach (TreeViewItem child in item.Items)
            {
                await UpdateUnreadCountsRecursive(child);
            }
        }

        // ウィンドウが表示された後に初回設定画面を表示（WPF仕様対応）
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // コンストラクタで読み込んだconfigは使えないので、ここで再取得
            var config = App.LoadConfig();

            if (config.FirstLaunch)
            {
                var settings = new SettingsWindow { Owner = this };  // ここでOwnerを設定しても安全
                settings.ShowDialog();

                config.FirstLaunch = false;
                App.SaveConfig(config);

            }
            // WebView2初期化と背景色設定
            await PreviewBrowser.EnsureCoreWebView2Async();

            // ダークテーマ判定（簡易：背景色で判断）
            var bg = (SolidColorBrush)System.Windows.Application.Current.Resources["WindowBackgroundBrush"];
            bool isDark = bg.Color.R < 128;

            // WebView2の背景色をテーマに合わせる
            PreviewBrowser.DefaultBackgroundColor = isDark
                ? System.Drawing.Color.FromArgb(255, 32, 32, 32)
                : System.Drawing.Color.White;
        }
    }
}