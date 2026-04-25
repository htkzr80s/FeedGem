using FeedGem.Data;
using System.Xml.Linq;

namespace FeedGem.Services
{
    public class OpmlService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // OPMLインポート
        public async Task<(int total, int added, int skipped)> ImportAsync(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var body = doc.Root?.Element("body");
            if (body == null) return (0, 0, 0);

            int total = 0;
            int added = 0;
            int skipped = 0;

            // 既存URLをハッシュセットで取得し、高速に重複チェックを行う
            var existingFeeds = await _repository.GetAllFeedsAsync();
            var existingUrls = new HashSet<string>(
                existingFeeds.Select(f => FeedUrlNormalizer.Normalize(f.Url))
            );

            // インポート処理を開始する。初期フォルダはルートとする
            await ProcessOutline(body.Elements("outline"), "/");

            async Task ProcessOutline(IEnumerable<XElement> elements, string currentPath)
            {
                foreach (var outline in elements)
                {
                    // text属性またはtitle属性から名称を取得。どちらもなければ「無題」とする
                    string title = outline.Attribute("text")?.Value
                                ?? outline.Attribute("title")?.Value
                                ?? "無題";

                    string xmlUrl = outline.Attribute("xmlUrl")?.Value ?? "";

                    // xmlUrl属性が存在する場合はフィードとして処理
                    if (!string.IsNullOrEmpty(xmlUrl))
                    {
                        total++;

                        string normalized = FeedUrlNormalizer.Normalize(xmlUrl);

                        // 登録済みURLであればスキップ
                        if (existingUrls.Contains(normalized))
                        {
                            skipped++;
                            continue;
                        }

                        // 決定されたcurrentPath（ルートまたは第一階層フォルダ）を使用して保存
                        var (feedId, isNew) = await _repository.AddFeedAsync(currentPath, title, normalized);

                        if (isNew)
                        {
                            added++;
                            existingUrls.Add(normalized); // 同一インポート内での重複防止
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    // 子要素を持つ場合はフォルダとして処理
                    else if (outline.Elements("outline").Any())
                    {
                        string nextPath;

                        // 現在がルート（/）の場合のみ、新しいフォルダ階層を認める
                        if (currentPath == "/")
                        {
                            // 第一階層のフォルダ名をパスとして設定
                            nextPath = title;
                        }
                        else
                        {
                            // すでにフォルダ内の場合は、サブフォルダを作らず現在のフォルダパスを維持
                            nextPath = currentPath;
                        }

                        // 再帰的に中身を処理。階層が深くなってもnextPathは第一階層のまま固定される
                        await ProcessOutline(outline.Elements("outline"), nextPath);
                    }
                }
            }

            return (total, added, skipped);
        }

        // OPMLエクスポート
        public async Task<XDocument> ExportAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();

            var root = new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head", new XElement("title", "FeedGem Export")),
                new XElement("body"));

            var body = root.Element("body");
            if (body == null) return new XDocument(root);

            var folders = feeds.GroupBy(f => f.FolderPath);

            foreach (var folder in folders)
            {
                XContainer target = body;

                if (folder.Key != "/")
                {
                    var folderNode = new XElement("outline",
                        new XAttribute("text", folder.Key.TrimStart('/')));

                    body.Add(folderNode);
                    target = folderNode;
                }

                foreach (var f in folder)
                {
                    if (f.Url.StartsWith("folder://")) continue;

                    target.Add(new XElement("outline",
                        new XAttribute("text", f.Title),
                        new XAttribute("title", f.Title),
                        new XAttribute("type", "rss"),
                        new XAttribute("xmlUrl", f.Url)));
                }
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        }
    }
}