using FeedGem.Models;
using System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public static class TreeViewItemFactory
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
                ? TreeViewHeaderFactory.Create(displayName, true)
                : TreeViewHeaderFactory.Create(displayName, false, node.Url);

            // TreeViewItem生成
            var item = new TreeViewItem
            {
                Header = header,
                Tag = node.FeedId != null
                    ? new TreeTag
                    {
                        Type = TreeNodeType.Feed,
                        FeedId = node.FeedId
                    }
                    : new TreeTag
                    {
                        Type = TreeNodeType.Folder,
                        FolderPath = node.Path
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