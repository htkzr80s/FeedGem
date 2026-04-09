using FeedGem.Models;
using System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public static class FeedTreeItem
    {
        // TreeNodeModel → TreeViewItem に変換
        public static TreeViewItem Create(TreeNodeModel node)
        {
            // 表示名（未読数付き）
            string displayName = node.UnreadCount > 0
                ? $"{node.Name} ({node.UnreadCount})"
                : node.Name;

            // ヘッダー生成
            var header = node.FeedId == null
                ? FeedTreeHeader.Create(displayName, true)
                : FeedTreeHeader.Create(displayName, false, node.Url);

            // TreeViewItem生成
            var item = new TreeViewItem
            {
                Header = header,
                Tag = node.FeedId != null
                    ? new TreeTag
                    {
                        Type = TreeNodeType.Feed,
                        FeedId = node.FeedId,
                        Name = node.Name, 
                        UnreadCount = node.UnreadCount,
                        Url = node.Url
                    }
                    : new TreeTag
                    {
                        Type = TreeNodeType.Folder,
                        FolderPath = node.Path,
                        Name = node.Name,
                        UnreadCount = node.UnreadCount
                    },
                IsExpanded = true
            };

            // 子ノード再帰生成
            foreach (var child in node.Children)
            {
                item.Items.Add(Create(child));
            }

            return item;
        }
    }
}