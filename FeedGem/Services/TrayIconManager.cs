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
        private readonly UnreadCountService _unreadService;

        private BitmapImage? _normalTrayIcon;
        private BitmapImage? _unreadTrayIcon;

        public TrayIconManager(TaskbarIcon taskbarIcon, MainWindow window, UnreadCountService unreadService)
        {
            _taskbarIcon = taskbarIcon;
            _window = window;
            _unreadService = unreadService;

            LoadIcons();
            _taskbarIcon.TrayLeftMouseUp += TaskbarIcon_TrayLeftMouseUp;
        }

        private void LoadIcons()
        {
            _normalTrayIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/app.ico"));
            _unreadTrayIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/unread.ico"));

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

        public async Task UpdateIconAsync()
        {
            if (_taskbarIcon == null) return;

            int totalUnread = await _unreadService.GetTotalUnreadAsync();
            _taskbarIcon.IconSource = totalUnread > 0 ? _unreadTrayIcon : _normalTrayIcon;
        }
    }
}