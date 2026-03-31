using FeedGem.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public class TreeBuilder(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // TreeView構築用データを生成する
        public async Task<List<TreeViewItem>> BuildTreeAsync()
        {
            var result = new List<TreeViewItem>();

            var feeds = await _repository.GetAllFeedsAsync();
            var folderNodes = new Dictionary<string, TreeViewItem>();

            foreach (var feed in feeds)
            {
                var pathParts = feed.FolderPath.Split(['/'], System.StringSplitOptions.RemoveEmptyEntries);
                ItemsControl? parent = null!;
                string currentKey = "";

                foreach (var part in pathParts)
                {
                    currentKey += "/" + part;

                    if (!folderNodes.TryGetValue(currentKey, out var node))
                    {
                        node = new TreeViewItem
                        {
                            Header = part,
                            Tag = currentKey,
                            IsExpanded = true
                        };

                        if (parent == null)
                            result.Add(node);
                        else
                            parent.Items.Add(node);

                        folderNodes[currentKey] = node;
                    }

                    parent = node;
                }

                // folder://はスキップ
                if (feed.Url.StartsWith("folder://"))
                    continue;

                var feedNode = new TreeViewItem
                {
                    Header = feed.Title,
                    Tag = feed.Id
                };

                if (parent == null)
                {
                    result.Add(feedNode);
                }
                else
                {
                    parent.Items.Add(feedNode);
                }
            }

            return result;
        }
    }
}