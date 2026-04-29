using FeedGem.Models;
using System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public static class FeedTreeItem
    {
        // TreeNodeModelからWPFのTreeViewItemを生成する
        public static TreeViewItem Create(TreeNodeModel node)
        {
            // 未読数がある場合は名前の横に件数を表示する
            string displayName = node.UnreadCount > 0
                ? $"{node.Name} ({node.UnreadCount})"
                : node.Name;

            // ヘッダー表示用の要素を作成する（フォルダかフィードかで切り替え）
            var header = node.IsFolder
                ? FeedTreeHeader.Create(displayName, true)
                : FeedTreeHeader.Create(displayName, false, node.Url, node.ErrorState);

            // TreeViewに表示する1項目（Item）を生成する
            var item = new TreeViewItem
            {
                Header = header,
                // 後で識別しやすいようにタグ情報を格納する
                Tag = node.IsFolder
                    ? new TreeTag
                    {
                        Id = node.Id,
                        Type = TreeNodeType.Folder,
                        FolderPath = node.Path,
                        Name = node.Name,
                        UnreadCount = node.UnreadCount
                    }
                    : new TreeTag
                    {
                        Id = node.Id,
                        Type = TreeNodeType.Feed,
                        Name = node.Name,
                        UnreadCount = node.UnreadCount,
                        Url = node.Url
                    },
                IsExpanded = true
            };

            // 子要素（サブフォルダやフィード）があれば再帰的に追加する
            foreach (var child in node.Children)
            {
                item.Items.Add(Create(child));
            }

            return item;
        }
    }
}