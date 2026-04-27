using FeedGem.Data;
using FeedGem.Models;

namespace FeedGem.UIHelpers
{
    public class TreeBuilder(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // TreeView構築用データを生成する
        public async Task<List<TreeNodeModel>> BuildTreeDataAsync()
        {
            var result = new List<TreeNodeModel>();

            var feeds = await _repository.GetAllFeedsAsync();
            var unreadMap = await _repository.GetUnreadCountMapAsync();
            var folderNodes = new Dictionary<string, TreeNodeModel>();

            foreach (var feed in feeds)
            {
                var pathParts = feed.FolderPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
                TreeNodeModel? parent = null;
                string currentKey = "";

                foreach (var part in pathParts)
                {
                    currentKey += "/" + part;

                    if (!folderNodes.TryGetValue(currentKey, out var node))
                    {
                        node = new TreeNodeModel
                        {
                            Name = part,
                            Path = currentKey
                        };

                        if (parent == null)
                            result.Add(node);
                        else
                            parent.Children.Add(node);

                        folderNodes[currentKey] = node;
                    }

                    parent = node;
                }

                // フォルダダミー
                if (feed.Url.StartsWith("folder://"))
                {
                    string folderName = feed.Title;
                    string folderPath = feed.FolderPath == "/" ? $"/{folderName}" : $"{feed.FolderPath}/{folderName}";

                    if (!folderNodes.ContainsKey(folderPath))
                    {
                        var folderNode = new TreeNodeModel
                        {
                            Name = folderName,
                            Path = folderPath
                        };

                        if (parent == null)
                            result.Add(folderNode);
                        else
                            parent.Children.Add(folderNode);

                        folderNodes[folderPath] = folderNode;
                    }

                    continue;
                }

                var feedNode = new TreeNodeModel
                {
                    Name = feed.Title,
                    FeedId = feed.Id,
                    Url = feed.Url,
                    UnreadCount = unreadMap.TryGetValue(feed.Id, out var count) ? count : 0,
                    ErrorState = feed.ErrorState
                };



                // デバッグ：ErrorStateが正しく詰まっているか確認する
                System.Diagnostics.Debug.WriteLine(
                    $"[TreeBuilder] {feed.Title} / ErrorState={feed.ErrorState}");
                // デバッグ：ErrorStateが正しく詰まっているか確認する



                if (parent == null)
                    result.Add(feedNode);
                else
                    parent.Children.Add(feedNode);
            }

            // フォルダ未読数を計算
            foreach (var node in result)
            {
                CalculateUnread(node);
            }

            return result;
        }

        // 子ノードの未読数を合計する
        private static int CalculateUnread(TreeNodeModel node)
        {
            int total = node.UnreadCount;

            foreach (var child in node.Children)
            {
                total += CalculateUnread(child);
            }

            node.UnreadCount = total;
            return total;
        }


    }
}