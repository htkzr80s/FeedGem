using FeedGem.Data;
using FeedGem.Views;
using H.NotifyIcon;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FeedGem.Services
{
    internal class TrayIconManager
    {
        private readonly TaskbarIcon? _taskbarIcon;
        private readonly MainWindow _window;
        private readonly UnreadCountService _unreadCountService;
        private readonly FeedRepository _repository;

        private BitmapImage? _normalTrayIcon;
        private BitmapImage? _unreadTrayIcon;
        private BitmapImage? _errorTrayIcon;

        public TrayIconManager(TaskbarIcon taskbarIcon, MainWindow window, UnreadCountService unreadService, FeedRepository repository)
        {
            _taskbarIcon = taskbarIcon;
            _window = window;
            _unreadCountService = unreadService;
            _repository = repository;

            LoadIcons();
            _taskbarIcon.TrayLeftMouseUp += TaskbarIcon_TrayLeftMouseUp;
        }

        private void LoadIcons()
        {
            _normalTrayIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/app.ico"));
            _unreadTrayIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/unread.ico"));
            _errorTrayIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/error.ico"));

            _taskbarIcon?.IconSource = _normalTrayIcon;
        }

        // 最小化時の処理（MainWindow側から呼ばれる）
        public void HandleMinimizeToTray()
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.Hide();
            }
        }

        // トレイアイコン左クリック → MainWindowのRestoreWindow()を呼び出す
        private void TaskbarIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            _window.RestoreWindow();
        }

        // エラー優先でアイコンを切り替える（error > unread > normal）
        public async Task UpdateIconAsync()
        {
            if (_taskbarIcon == null) return;

            // エラーフィードが1件でもあれば error.ico を優先表示する
            bool hasError = await _repository.HasAnyFeedErrorAsync();
            if (hasError)
            {
                _taskbarIcon.IconSource = _errorTrayIcon;
                return;
            }

            int totalUnread = await _unreadCountService.GetTotalUnreadAsync();
            _taskbarIcon.IconSource = totalUnread > 0 ? _unreadTrayIcon : _normalTrayIcon;
        }
    }
}