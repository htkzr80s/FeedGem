using System;

namespace FeedGem.Services
{
    public class NotificationService(Action onTrayLeftClick) : IDisposable
    {
        private readonly TrayIconManager _tray = new(onTrayLeftClick);

        // 未読状態を反映
        public void UpdateUnreadState(int unreadCount)
        {
            _tray.SetUnreadState(unreadCount > 0);
        }

        public void Dispose()
        {
            _tray.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}