using FeedGem.Models;
using System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    // ツリー項目の生成と状態管理を担当するクラス
    public static class FeedTreeItem
    {
        // TreeViewから現在開いている項目のID一覧を抽出する
        public static HashSet<long> GetExpandedIds(ItemCollection items)
        {
            var expandedIds = new HashSet<long>();
            // 再帰的にチェックを開始
            CollectExpandedIds(items, expandedIds);
            return expandedIds;
        }

        // 内部用：再帰的に展開状態をチェックする
        private static void CollectExpandedIds(ItemCollection items, HashSet<long> expandedIds)
        {
            // TreeView内の各項目を確認
            foreach (TreeViewItem item in items)
            {
                // 項目が展開されており、かつTagにIDが記録されているか確認
                if (item.IsExpanded && item.Tag is TreeTag tag)
                {
                    // セットにIDを保存
                    expandedIds.Add(tag.Id);
                }

                // 子要素に対しても同じ処理を繰り返す
                if (item.Items.Count > 0)
                {
                    CollectExpandedIds(item.Items, expandedIds);
                }
            }
        }

        // データをTreeViewItemに変換する。保存されたIDがあれば展開状態を復元する
        public static TreeViewItem Create(TreeNodeModel node, HashSet<long>? expandedIds = null)
        {
            // 未読数がある場合は名前の横に件数を表示する
            string displayName = node.UnreadCount > 0
                ? $"{node.Name} ({node.UnreadCount})"
                : node.Name;

            // フォルダかフィードかに応じてヘッダー要素を生成する
            var header = node.IsFolder
                ? FeedTreeHeader.Create(displayName, true)
                : FeedTreeHeader.Create(displayName, false, node.Url, node.ErrorState);

            // 保存された展開リストに自身のIDが含まれているか確認
            bool shouldExpand = node.IsFolder && expandedIds != null && expandedIds.Contains(node.Id);

            // TreeViewItemのインスタンスを作成
            var item = new TreeViewItem
            {
                Header = header, // UI要素をセット
                IsExpanded = shouldExpand, // 以前の状態を適用
                Tag = new TreeTag // 識別情報を格納
                {
                    Id = node.Id,
                    ParentId = node.ParentId,
                    Type = node.IsFolder ? TreeNodeType.Folder : TreeNodeType.Feed,
                    Name = node.Name,
                    UnreadCount = node.UnreadCount,
                    SortOrder = node.SortOrder,
                    Url = node.Url
                },
            };

            // 子ノードがある場合は再帰的に追加
            foreach (var child in node.Children)
            {
                // 子の生成時にも展開リストを引き継ぐ
                item.Items.Add(Create(child, expandedIds));
            }

            return item;
        }
    }
}