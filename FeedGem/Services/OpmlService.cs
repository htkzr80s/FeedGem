using FeedGem.Data;
using FeedGem.Models;
using System.Xml.Linq;

namespace FeedGem.Services
{
    public class OpmlService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // OPMLファイルを読み込み、データベースへインポートする
        public async Task<(int total, int added, int skipped)> ImportAsync(string filePath)
        {
            // XMLファイルをロード
            var doc = XDocument.Load(filePath);
            var body = doc.Root?.Element("body");

            // body要素が存在しない場合は空の結果を返す
            if (body == null) return (0, 0, 0);

            int total = 0;
            int added = 0;
            int skipped = 0;

            // データベースから既存のフィードURLを全件取得
            var existingFeeds = await _repository.GetAllFeedsAsync();

            // 高速な検索のためにハッシュセットへ格納
            var existingUrls = new HashSet<string>(
                existingFeeds.Select(f => FeedUrlNormalizer.Normalize(f.Url))
            );

            // 再帰的に要素を処理する内部関数
            async Task ProcessOutlineSemiFlattened(IEnumerable<XElement> elements, long? currentParentId = null)
            {
                foreach (var outline in elements)
                {
                    // textまたはtitle属性から表示名を取得
                    string title = (outline.Attribute("text")?.Value
                                ?? outline.Attribute("title")?.Value
                                ?? "NoTitle").Trim();

                    // XML配信用のURLを取得
                    string xmlUrl = (outline.Attribute("xmlUrl")?.Value ?? "").Trim();

                    if (!string.IsNullOrEmpty(xmlUrl))
                    {
                        // URLが存在する場合（フィード項目）
                        total++;
                        string normalized = FeedUrlNormalizer.Normalize(xmlUrl);

                        // 重複チェックを行い、未登録の場合のみ追加
                        if (!existingUrls.Contains(normalized))
                        {
                            await _repository.AddFeedAsync(currentParentId, title, normalized);
                            existingUrls.Add(normalized); // 同一ファイル内の重複対策
                            added++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    else
                    {
                        // URLがない場合（フォルダ項目）
                        var children = outline.Elements("outline");
                        if (!children.Any()) continue;

                        if (currentParentId == null)
                        {
                            // ルート階層なら新しいフォルダを作成
                            var (folderId, _) = await _repository.AddFeedAsync(null, title, "");

                            // 子要素をそのフォルダ配下として処理
                            await ProcessOutlineSemiFlattened(children, folderId);
                        }
                        else
                        {
                            // 既にサブフォルダ内なら、階層を深くせず現在のフォルダに子要素を展開
                            await ProcessOutlineSemiFlattened(children, currentParentId);
                        }
                    }
                }
            }

            // 初回呼び出し
            await ProcessOutlineSemiFlattened(body.Elements("outline"), null);

            return (total, added, skipped);
        }

        // OPMLエクスポート
        public async Task<XDocument> ExportAsync()
        {
            // データベースからすべてのフィードとフォルダの情報を取得する
            var allItems = await _repository.GetAllFeedsAsync();

            // OPMLの基本構造（ルート、ヘッダー、ボディ）を定義する
            var root = new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head", new XElement("title", "FeedGem Export")),
                new XElement("body"));

            var body = root.Element("body");
            // 構造に問題がある場合は空のドキュメントを返す
            if (body == null) return new XDocument(root);

            // まず、ルート階層（親IDがnull）にあるアイテムを抽出する
            var rootItems = allItems.Where(f => f.ParentId == null).OrderBy(f => f.SortOrder);

            foreach (var item in rootItems)
            {
                // URLが空でない場合は、ルート直下のフィードとして追加する
                if (!string.IsNullOrEmpty(item.Url))
                {
                    body.Add(CreateFeedOutline(item));
                }
                else
                {
                    // URLが空の場合はフォルダとして扱い、その中身も抽出する
                    var folderNode = new XElement("outline", new XAttribute("text", item.Title));

                    // このフォルダを親に持つ（ParentIdがこのフォルダのIdと一致する）アイテムを取得
                    var children = allItems.Where(f => f.ParentId == item.Id).OrderBy(f => f.SortOrder);

                    foreach (var child in children)
                    {
                        // 子要素がフィードであればフォルダノードの中に追加する
                        if (!string.IsNullOrEmpty(child.Url))
                        {
                            folderNode.Add(CreateFeedOutline(child));
                        }
                        // 現在の仕様では階層は1段までのため、子フォルダの再帰処理は行わない
                    }

                    // 中身が存在するフォルダ、または空でもフォルダとして存在させる場合はbodyに追加
                    body.Add(folderNode);
                }
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        }

        // フィード情報をOPMLのoutline要素に変換するための補助メソッド
        private static XElement CreateFeedOutline(FeedInfo f)
        {
            // RSSフィードとして標準的な属性をセットする
            return new XElement("outline",
                new XAttribute("text", f.Title),
                new XAttribute("title", f.Title),
                new XAttribute("type", "rss"),
                new XAttribute("xmlUrl", f.Url));
        }
    }
}