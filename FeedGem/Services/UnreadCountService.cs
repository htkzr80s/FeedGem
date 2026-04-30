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

        // ツリーの各項目を再帰的に巡回し、未読数を反映させる
        private static async Task UpdateItemRecursive(TreeViewItem item, Dictionary<long, int> unreadMap)
        {
            if (item.Tag is TreeTag tag && tag.Type == TreeNodeType.Feed && tag.Id != 0)
            {
                // マップから該当IDの未読数を取得。存在しない場合は0とする
                int unread = unreadMap.TryGetValue(tag.Id, out int count) ? count : 0;

                // 保持している未読数と差分がある場合のみ、UI表示を更新する
                if (tag.UnreadCount != unread)
                {
                    tag.UnreadCount = unread;

                    string displayName = unread > 0 ? $"{tag.Name} ({unread})" : tag.Name;

                    item.Header = FeedTreeHeader.Create(
                        displayName,
                        tag.Type == TreeNodeType.Folder,
                        tag.Url
                    );
                }
            }

            // 子階層の項目に対しても同様の処理を繰り返す
            foreach (TreeViewItem child in item.Items)
            {
                await UpdateItemRecursive(child, unreadMap);
            }
        }
    }
}