using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using Forms = System.Windows.Forms;

namespace FeedGem.Services
{
    public class TrayIconManager : IDisposable
    {
        private Forms.NotifyIcon? _notifyIcon;
        private Icon? _normalIcon;
        private Icon? _unreadIcon;

        private readonly Action _onLeftClick;

        // コンストラクタ
        public TrayIconManager(Action onLeftClick)
        {
            _onLeftClick = onLeftClick;

            LoadIcons();
            InitializeNotifyIcon();
        }

        // 埋め込みリソースからアイコン読み込み
        private void LoadIcons()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var normalStream = assembly.GetManifestResourceStream("FeedGem.Resources.app.ico");
            if (normalStream != null)
                _normalIcon = new Icon(normalStream);

            using var unreadStream = assembly.GetManifestResourceStream("FeedGem.Resources.unread.ico");
            if (unreadStream != null)
                _unreadIcon = new Icon(unreadStream);
        }

        // NotifyIcon初期化
        private void InitializeNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _normalIcon,
                Visible = true,
                Text = "FeedGem"
            };

            // 左クリックで復帰
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    _onLeftClick?.Invoke();
                }
            };
        }

        // 未読状態に応じてアイコン切り替え
        public void SetUnreadState(bool hasUnread)
        {
            _notifyIcon!.Icon = hasUnread ? _unreadIcon : _normalIcon;
        }

        // 終了時の解放
        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}