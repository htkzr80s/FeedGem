using FeedGem.Data;
using FeedGem.Models;
using FeedGem.UIHelpers;
using System.Windows.Controls;

namespace FeedGem.Services
{
    public class UnreadCountService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // 全フィードの未読件数を合計する
        public async Task<int> GetTotalUnreadAsync()
        {
            var map = await _repository.GetUnreadCountMapAsync();
            return map.Values.Sum();
        }

        // ツリービュー全体の未読表示を更新する
        public async Task UpdateAllTreeViewUnreadCountsAsync(ItemCollection topLevelItems)
        {
            var unreadMap = await _repository.GetUnreadCountMapAsync();

            foreach (TreeViewItem item in topLevelItems)
            {
                await UpdateItemRecursive(item, unreadMap);
            }
        }

        // ツリーの各項目を再帰的に巡回し、未読数を反映させる。戻り値はその項目の最終的な未読数
        private static async Task<int> UpdateItemRecursive(TreeViewItem item, Dictionary<long, int> unreadMap)
        {
            if (item.Tag is not TreeTag tag) return 0;

            int currentUnread;

            if (tag.Type == TreeNodeType.Feed)
            {
                // フィードの場合はDBから取得した値をそのまま使用する
                currentUnread = unreadMap.TryGetValue(tag.Id, out int count) ? count : 0;
            }
            else
            {
                // フォルダの場合は子要素の未読数をすべて合計する
                int totalChildUnread = 0;
                foreach (TreeViewItem child in item.Items)
                {
                    totalChildUnread += await UpdateItemRecursive(child, unreadMap);
                }
                currentUnread = totalChildUnread;
            }

            // 保持している未読数と差分がある場合のみ、UI表示を更新する
            if (tag.UnreadCount != currentUnread)
            {
                tag.UnreadCount = currentUnread;

                string displayName = currentUnread > 0 ? $"{tag.Name} ({currentUnread})" : tag.Name;

                // ヘッダーの再生成（FeedTreeHeaderは既存の仕組みを利用）
                item.Header = FeedTreeHeader.Create(
                    displayName,
                    tag.Type == TreeNodeType.Folder,
                    tag.Url
                );
            }

            return currentUnread;
        }
    }
}