using FeedGem.Data;
using FeedGem.Models;

namespace FeedGem.UIHelpers
{
    public class TreeBuilder(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // データベースから取得したフラットなリストを、階層構造（ツリー）に変換する
        public async Task<List<TreeNodeModel>> BuildTreeDataAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();
            var unreadMap = await _repository.GetUnreadCountMapAsync();

            // 全ノードを一時的に格納する辞書（親子紐付け用）
            var allNodes = new Dictionary<long, TreeNodeModel>();
            // 最上位階層（親がいない）ノードのリスト
            var rootNodes = new List<TreeNodeModel>();

            // 1. まず全項目をTreeNodeModelに変換して辞書に登録する
            foreach (var feed in feeds)
            {
                var node = new TreeNodeModel
                {
                    Id = feed.Id,
                    ParentId = feed.ParentId == 0 ? null : feed.ParentId, // 0は親なし（null）として扱う
                    Name = feed.Title,
                    IsFolder = string.IsNullOrEmpty(feed.Url), // URLが空ならフォルダと判定
                    Url = feed.Url,
                    SortOrder = feed.SortOrder,
                    ErrorState = feed.ErrorState,
                    // フィード単体の未読数を設定（フォルダなら後で再計算される）
                    UnreadCount = unreadMap.TryGetValue(feed.Id, out var count) ? count : 0
                };
                allNodes[node.Id] = node;
            }

            // 2. 親子関係を構築し、並び順（SortOrder）で整列させる
            foreach (var node in allNodes.Values.OrderBy(n => n.SortOrder))
            {
                // 親IDがない場合はルートリストに追加する
                if (node.ParentId == null)
                {
                    rootNodes.Add(node);
                }
                // 親が存在する場合は、その親のChildrenリストに追加する
                else if (allNodes.TryGetValue(node.ParentId.Value, out var parent))
                {
                    parent.Children.Add(node);
                }
            }

            // 3. フォルダの未読合計数を再計算する
            foreach (var root in rootNodes)
            {
                CalculateUnread(root);
            }

            return rootNodes;
        }

        // ノード配下の未読数を再帰的に集計する
        private static int CalculateUnread(TreeNodeModel node)
        {
            // 子要素がなければ現在の未読数をそのまま返す
            if (node.Children.Count == 0) return node.UnreadCount;

            // 子要素の未読数をすべて足し合わせる
            int total = node.UnreadCount;
            foreach (var child in node.Children)
            {
                total += CalculateUnread(child);
            }

            // 集計結果を自分の未読数として上書きする
            node.UnreadCount = total;
            return total;
        }
    }
}