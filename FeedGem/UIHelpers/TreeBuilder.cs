using FeedGem.Data;
using FeedGem.Models;

namespace FeedGem.UIHelpers
{
    public class TreeBuilder(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // データベースから取得したフィード情報を、UI表示用の階層構造（ツリー）データに変換する
        public async Task<List<TreeNodeModel>> BuildTreeDataAsync()
        {
            var result = new List<TreeNodeModel>();

            // フィード一覧と各フィードの未読件数を非同期で取得する
            var feeds = await _repository.GetAllFeedsAsync();
            var unreadMap = await _repository.GetUnreadCountMapAsync();
            var folderNodes = new Dictionary<string, TreeNodeModel>();

            // 取得した全フィードを1つずつ処理し、ツリー構造を組み立てる
            foreach (var feed in feeds)
            {
                // フォルダパスを「/」で分割し、親から順に階層を辿る
                var pathParts = feed.FolderPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
                TreeNodeModel? parent = null;
                string currentKey = "";

                // パスの各階層（フォルダ）について、存在確認と作成を行う
                foreach (var part in pathParts)
                {
                    currentKey += "/" + part;

                    // 該当するパスのフォルダノードがまだ作成されていない場合のみ新規作成する
                    if (!folderNodes.TryGetValue(currentKey, out var node))
                    {
                        node = new TreeNodeModel
                        {
                            Name = part,
                            Path = currentKey
                        };

                        // 親がいなければルート（一番上）に追加し、いればその子として追加する
                        if (parent == null)
                            result.Add(node);
                        else
                            parent.Children.Add(node);

                        folderNodes[currentKey] = node;
                    }

                    parent = node;
                }

                // フィード自体が「フォルダ」を定義するためのデータである場合の処理
                if (feed.Url.StartsWith("folder://"))
                {
                    // DBのルールに従い、このフォルダ自体のフルパスを「親のパス + 自身のタイトル」で特定する
                    // 親が "/" の場合は、単純に "/" + Title にする
                    string currentFolderPath = feed.FolderPath.EndsWith("/")
                        ? $"{feed.FolderPath}{feed.Title}"
                        : $"{feed.FolderPath}/{feed.Title}";

                    // すでに上位の階層処理で同じパスのノードが作られていないか確認する
                    if (!folderNodes.TryGetValue(currentFolderPath, out var folderNode))
                    {
                        folderNode = new TreeNodeModel
                        {
                            Name = feed.Title,
                            Path = currentFolderPath,
                            IsFolder = true,
                            Id = feed.Id // フォルダ自体のIDを保持させる
                        };

                        // 親ノード（parent）の下に追加する
                        if (parent == null)
                            result.Add(folderNode);
                        else
                            parent.Children.Add(folderNode);

                        // 名簿（Dictionary）に登録して、後の処理で参照できるようにする
                        folderNodes[currentFolderPath] = folderNode;
                    }
                    else
                    {
                        // すでにノードが存在する場合は、DBから取得した正しいIDを上書きして紐付ける
                        folderNode.Id = feed.Id;
                        folderNode.IsFolder = true;
                    }

                    continue;
                }

                // 通常のフィード（Webサイト）をノードとして作成し、親フォルダの下に追加する
                var feedNode = new TreeNodeModel
                {
                    Name = feed.Title,
                    Id = feed.Id,
                    Url = feed.Url,
                    Path = feed.FolderPath,
                    UnreadCount = unreadMap.TryGetValue(feed.Id, out var count) ? count : 0,
                    ErrorState = feed.ErrorState
                };

                // 親階層の有無によって、リストのどこに追加するかを振り分ける
                if (parent == null)
                {
                    // FolderPathが "/" の場合、ここにくる（一番上の階層に追加）
                    result.Add(feedNode);
                }
                else
                {
                    // FolderPathが "/フォルダ名" の場合、直前に作成・特定した親フォルダの中に追加する
                    parent.Children.Add(feedNode);
                }
            }

            // ツリーの構築完了後、各フォルダの未読合計数を再帰的に計算する
            foreach (var node in result)
            {
                CalculateUnread(node);
            }

            return result;
        }

        // 指定されたノードとそのすべての子ノードの未読数を合計して算出する
        private static int CalculateUnread(TreeNodeModel node)
        {
            // 自分自身の未読数を初期値にする
            int total = node.UnreadCount;

            // 子要素を一つずつ巡回し、その未読数を自分に加算していく（再帰呼び出し）
            foreach (var child in node.Children)
            {
                total += CalculateUnread(child);
            }

            // 最終的な合計値をノードのプロパティに保存する
            node.UnreadCount = total;
            return total;
        }
    }
}