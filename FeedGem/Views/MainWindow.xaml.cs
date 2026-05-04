using FeedGem.Core;
using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.UIHelpers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using static FeedGem.Services.LocalizationService;

namespace FeedGem.Views
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ArticleItem> currentArticles = [];

        // データベース操作を専門に行うインスタンス
        private readonly FeedRepository _repository;
        private readonly FeedService _feedService;
        private readonly TreeBuilder _treeBuilder;
        private readonly ContextMenuBuilder _menuBuilder;
        private readonly TreeDragDropHandler _dragHandler;
        private readonly FeedUpdateService _updateService;
        private readonly OpmlService _opmlService;
        private readonly UrlSubscriptionService _subscriptionService;
        private readonly BackgroundUpdateTimer _backgroundTimer;
        private long? _currentSelectedFeedId;
        private readonly TrayIconManager _trayManager;
        private readonly UnreadCountService _unreadCountService;

        public MainWindow()
        {
            InitializeComponent();

            // UIとウィンドウの初期設定を行う
            SetupWindowAppearance();

            // データベースパスの決定とサービスの初期化を行う
            string dbPath = EnsureAndGetDatabasePath();
            _repository = new FeedRepository(dbPath);
            _unreadCountService = new UnreadCountService(_repository);
            _feedService = new FeedService(_repository);
            _updateService = new FeedUpdateService(_repository, _feedService);
            _treeBuilder = new TreeBuilder(_repository);
            _opmlService = new OpmlService(_repository);
            _subscriptionService = new UrlSubscriptionService(_repository);
            _trayManager = new TrayIconManager(TaskbarIcon, this, _unreadCountService, _repository);

            _menuBuilder = new ContextMenuBuilder(
                _feedService,
                _updateService,
                LogTextBlock,
                UpdateLastUpdateTime,
                ImportOpmlAsync,
                ExportOpmlAsync
            );

            _feedService.DataChanged += async () =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    await LoadFeedsToTreeViewAsync();
                    await RefreshCurrentArticleListAsync();
                });
            };

            // 更新完了イベントに、UIを更新するメソッドを紐付ける
            _updateService.AllUpdatesCompleted += (s, e) => 
            {
                // UIの更新はUIスレッドで行う必要があるため、Dispatcherを介する
                Dispatcher.Invoke(() => 
                {
                    UpdateLastUpdateTime();
                });
            };

            _backgroundTimer = new BackgroundUpdateTimer(
                _updateService,
                async () => await LoadFeedsToTreeViewAsync(),
                this.Dispatcher
            );

            _dragHandler = new TreeDragDropHandler(
                _repository,
                LoadFeedsToTreeViewAsync
            );

            // 各種イベントの購読と設定
            this.StateChanged += MainWindow_StateChanged;
            ArticleListView.ItemsSource = currentArticles;
            ThemeManager.ThemeChanged += OnThemeChanged;

            // ウィンドウのUI描画が完了した時のイベントを登録
            this.Loaded += MainWindow_Loaded;
        }

        // ウィンドウの位置やサイズ、アイコンの初期設定
        private void SetupWindowAppearance()
        {
            // Configからウィンドウ設定を読み込み、位置・サイズ・カラム幅を復元
            var config = App.LoadConfig();
            this.Left = config.WindowLeft;
            this.Top = config.WindowTop;
            this.Width = config.WindowWidth;
            this.Height = config.WindowHeight;

            // XAMLでx:Nameを付けたColumnDefinitionに幅を復元
            colFeedTree?.Width = new GridLength(config.FeedTreeWidth);
            colArticleList?.Width = new GridLength(config.ArticleListWidth);

            // マルチモニタ対策：ウィンドウが表示されない位置ならプライマリ中央へ
            App.EnsureWindowOnScreen(this);

            SetupWindowIcon();
            ApplyTranslations();
        }

        // ユーザーデータフォルダを確認し、データベースのフルパスを返す
        private static string EnsureAndGetDatabasePath()
        {
            // EXEがある場所を取得
            string baseDir = AppContext.BaseDirectory;
            string userDataDir = Path.Combine(baseDir, AppConstants.UserDataFolderName);

            // フォルダが存在しなければ作成
            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }

            // ファイルのフルパスを生成して返す
            return Path.Combine(userDataDir, AppConstants.DatabaseFileName);
        }

        // ウィンドウの描画が完了した後に呼ばれるイベント
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // スケルトン（ダミー項目）を表示
            ShowSkeletonLoaders();

            try
            {
                // UIスレッドをブロックしないよう、DBの初期化を別スレッドで実行
                await Task.Run(() => _repository.Initialize());

                // 起動時にデータを画面に反映させる
                await LoadFeedsToTreeViewAsync();
                await _unreadCountService.UpdateAllTreeViewUnreadCountsAsync(FeedTreeView.Items);

                // スケルトン（ダミー項目）を消去する
                currentArticles.Clear();

                // トレイアイコンを更新
                await _trayManager.UpdateIconAsync();

                // バックグラウンドでの更新処理を開始
                _ = _updateService.UpdateAllAsync();

                // 1時間ごとに実行
                _backgroundTimer.Start(TimeSpan.FromHours(1));

                // WebView2の初期化処理を非同期で開始
                _ = InitializeWebViewAsync();

                LogTextBlock.Text = "準備が完了しました。";
            }
            catch (Exception)
            {
                LogTextBlock.Text = "アプリの起動に失敗しました。一部機能が制限されます。";
            }
        }

        // 記事リストにスケルトンUI（ダミー項目）を表示する
        private void ShowSkeletonLoaders()
        {
            currentArticles.Clear();

            // 定数で指定した回数分、読み込み中状態のダミーデータを追加
            for (int i = 0; i < AppConstants.SkeletonLoaderCount; i++)
            {
                currentArticles.Add(new ArticleItem
                {
                    Title = "_Loading_",
                    Summary = "Preparation in progress. Please wait a moment.",
                    IsRead = true // 未読バッジが出ないようにしておく
                });
            }
        }

        // 翻訳辞書の内容を画面上の要素に適用するメソッド
        private void ApplyTranslations()
        {
            TraymenuShow.Header = T("MainWindow.Tray.Menu.Show");
            TraymenuMini.Header = T("MainWindow.Tray.Menu.Minimize");
            TraymenuExit.Header = T("MainWindow.Tray.Menu.Exit");
            CtxArticleOpenBrowser.Header = T("ArticleView.CtxM.Open.Browser");
            CtxArticleCopyTitle.Header = T("ArticleView.CtxM.Copy.ArticleTitle");
            CtxArticleCopyUrl.Header = T("ArticleView.CtxM.Copy.ArticleUrl");
            OpenBrowserButton.Content = T("MainWindow.Btn.Open.Browser");
            SearchBox.Text = T("MainWindow.Bar.Box.Url");
            UrlEnterButton.ToolTip = T("MainWindow.Tip.Btn.Subscribe");
            FilterBox.Text = T("MainWindow.Bar.Box.Filter");
            FilterClearButton.ToolTip = T("MainWindow.Tip.Btn.FilterClear");
            SettingsButton.ToolTip = T("MainWindow.Tip.Btn.Settings");
        }

        // WebView2の初期化と、新規ページ読み込み時のスタイル自動適用を行う
        private async Task InitializeWebViewAsync()
        {
            try
            {
                // WebView2の準備を待機する
                await PreviewBrowser.EnsureCoreWebView2Async(null);

                var config = App.LoadConfig();
                string theme = config.Theme == "Auto"
                    ? ThemeManager.GetSystemTheme()
                    : config.Theme;

                // 背景色の設定
                PreviewBrowser.DefaultBackgroundColor =
                    theme == "Dark"
                    ? System.Drawing.Color.FromArgb(255, 32, 32, 32)
                    : System.Drawing.Color.White;

                // 初回CSS（軽量版）
                await WebViewThemeService.InitializeAsync(PreviewBrowser.CoreWebView2, theme);

                // ナビゲーション後に毎回再適用
                PreviewBrowser.CoreWebView2.NavigationCompleted += async (s, e) =>
                {
                    try
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
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to set theme on navigation: {ex.Message}");
                    }     
                };

                // 初回表示にも適用
                await WebViewThemeService.ApplyAsync(PreviewBrowser.CoreWebView2, theme);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");

                MessageBox.Show(
                    "Failed to load the browser component.\n" +
                    "If the problem persists, please reinstall the Microsoft Edge WebView2 Runtime.\n\n" +
                    "https://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 Initialization Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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

        // 最小化時にトレイへ格納する
        private void MainWindow_StateChanged(object? sender, EventArgs? e)
        {
            // 最小化されたでトレイに格納
            if (this.WindowState == WindowState.Minimized)
            {
                _trayManager.HandleMinimizeToTray();
            }
        }

        // ウィンドウ復帰
        public void RestoreWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        // トレイメニューの「表示」がクリックされた時の処理
        private void TrayMenu_Show_Click(object sender, RoutedEventArgs e)
        {
            RestoreWindow();
        }

        // トレイメニューの「最小化」がクリックされた時の処理
        private void TrayMenu_Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // トレイメニューの「終了」がクリックされた時の処理
        private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentConfig();
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

        // 記事リストの選択が変更された際の処理
        private async void ArticleListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArticleListView.SelectedItem is ArticleItem selectedArticle)
            {
                // 既読処理
                if (!selectedArticle.IsRead)
                {
                    await _feedService.MarkArticleAsReadAsync(selectedArticle);
                    await _trayManager.UpdateIconAsync();
                    await _unreadCountService.UpdateAllTreeViewUnreadCountsAsync(FeedTreeView.Items);
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
            if (SearchBox.Text == "URLを入力")
            {
                SearchBox.Text = ""; // ヒント文字を消す
                SearchBox.Foreground = (Brush)FindResource("TextBrush");
            }
        }

        // テキストボックス（SearchBox）でキーが押された時の処理
        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
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
            if (string.IsNullOrEmpty(url) || url == "URLを入力") return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                LogTextBlock.Text = "フィードを探索中...";

                var candidates = await FeedDiscoveryService.DiscoverFeedsAsync(url);

                Mouse.OverrideCursor = null;

                var result = await _subscriptionService.HandleCandidatesAsync(candidates);

                if (result == SubscribeResult.NeedsSelection)
                {
                    var window = new FeedSelectionWindow(candidates) { Owner = this };

                    if (window.ShowDialog() == true)
                    {
                        var selected = window.SelectedFeeds.FirstOrDefault();
                        if (selected != null)
                        {
                            result = await _subscriptionService.AddFeedAsync(selected);
                        }
                        else
                        {
                            result = SubscribeResult.Error;
                        }
                    }
                    else
                    {
                        LogTextBlock.Text = "購読を中止しました。";
                        return;
                    }
                }

                await HandleSubscribeResultAsync(result);
            }
            finally
            {
                SearchBox.Text = "URLを入力";
                Mouse.OverrideCursor = null;
            }
        }

        // 結果に応じたUI処理
        private async Task HandleSubscribeResultAsync(SubscribeResult result)
        {
            switch (result)
            {
                case SubscribeResult.Success:
                    LogTextBlock.Text = "フィードを追加しました。";
                    await LoadFeedsToTreeViewAsync();
                    break;

                case SubscribeResult.AlreadySubscribed:
                    LogTextBlock.Text = "既に登録済みのため中止しました。";
                    MessageBox.Show("既に登録済みです。");
                    break;

                case SubscribeResult.SkippedOrEmpty:
                    LogTextBlock.Text = "未対応のURLのため購読を中止しました。";
                    MessageBox.Show("未対応のURLのため購読を中止しました。");
                    break;

                case SubscribeResult.Error:
                    LogTextBlock.Text = "エラー (未対応のURL等)";
                    MessageBox.Show("エラーで登録できません (未対応のURL等)");
                    break;

                case SubscribeResult.NoCandidates:
                    LogTextBlock.Text = "フィードが見つかりませんでした。";
                    MessageBox.Show("フィードが見つかりませんでした。");
                    break;

                case SubscribeResult.TooManyCandidates:
                    LogTextBlock.Text = "購読URLが多すぎるため処理を中止しました。";
                    MessageBox.Show("購読URLが多すぎるため処理を中止しました。");
                    break;
            }
        }

        // 記事検索ボックスにフォーカスが当たった時の処理
        private void FilterBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (FilterBox.Text == "記事を検索")
            {
                FilterBox.Text = "";
                FilterBox.Foreground = (Brush)FindResource("TextBrush");
            }
        }

        // 記事検索ボックスの文字が変更された時の処理（絞り込み）
        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ListViewのデフォルトビューを取得してフィルタリング
            var view = CollectionViewSource.GetDefaultView(ArticleListView.ItemsSource);
            if (view == null) return;

            string keyword = FilterBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword) || keyword == "記事を検索")
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
            FilterBox.Text = "記事を検索";
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
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "OPML File (*.opml;*.xml)|*.opml;*.xml"
            };

            if (dialog.ShowDialog() != true) return;

            LogTextBlock.Text = "インポート中...";

            try
            {
                var (total, added, skipped) = await _opmlService.ImportAsync(dialog.FileName);

                // ・結果表示
                LogTextBlock.Text =
                    $"試行: {total} / 登録: {added} / スキップ: {skipped}";

                await LoadFeedsToTreeViewAsync();
            }
            catch (Exception ex)
            {
                LogTextBlock.Text = "OPMLのインポートに失敗しました。";
                LoggingService.Error("Failed to import OPML", ex);
                MessageBox.Show($"OPMLのインポートに失敗しました");
            }
        }

        // OPMLエクスポート
        private async Task ExportOpmlAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "OPML File (*.opml)|*.opml", FileName = "Myfeeds.opml" };
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
                LogTextBlock.Text = "OPMLのエクスポートに失敗しました。";
                LoggingService.Error("Failed to export OPML", ex);
                MessageBox.Show($"OPMLのエクスポートに失敗しました。");
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
        private void FeedTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _dragHandler.OnMouseMove(sender, e);
        }

        // ドラッグ中のマウスカーソル状態
        private void FeedTreeView_DragOver(object sender, DragEventArgs e)
        {
            TreeDragDropHandler.OnDragOver(e);
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
            await _trayManager.UpdateIconAsync();
        }

        // TreeViewItemに選択イベントを再帰的に付与する
        private void AttachSelectionHandler(TreeViewItem item)
        {
            if (item.Tag is TreeTag tag)
            {
                long targetId = tag.Id;
                var type = tag.Type;

                item.Selected += async (s, e) =>
                {
                    e.Handled = true;
                    _currentSelectedFeedId = targetId;

                    await LoadEntriesToListViewAsync(targetId);
                };
            }

            // 子要素にも再帰的にハンドラを付与する
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                {
                    AttachSelectionHandler(childItem);
                }
            }
        }

        // 記事リストを表示する（フォルダ・フィード共通）
        private async Task LoadEntriesToListViewAsync(long targetId)
        {
            ArticleListView.ItemsSource = null;
            currentArticles.Clear();

            try
            {
                var articles = await _feedService.GetEntriesAsync(targetId);

                foreach (var article in articles)
                {
                    currentArticles.Add(article);
                }
                ArticleListView.ItemsSource = currentArticles;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"記事の読み込みに失敗しました: {ex.Message}", "Load Error");
            }
        }

        private async Task RefreshCurrentArticleListAsync()
        {
            // 何かが選択されている場合のみ処理を実行する
            if (_currentSelectedFeedId.HasValue)
            {
                await LoadEntriesToListViewAsync(_currentSelectedFeedId.Value);
            }
        }
    }
}