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

            // ・既存URL一覧を取得（高速比較用）
            var existingFeeds = await _repository.GetAllFeedsAsync();
            var existingUrls = new HashSet<string>(
                existingFeeds.Select(f => FeedUrlNormalizer.Normalize(f.Url))
            );

            await ProcessOutline(body.Elements("outline"), "/");

            async Task ProcessOutline(IEnumerable<XElement> elements, string currentPath)
            {
                foreach (var outline in elements)
                {
                    string title = outline.Attribute("text")?.Value
                                ?? outline.Attribute("title")?.Value
                                ?? "無題";

                    string xmlUrl = outline.Attribute("xmlUrl")?.Value ?? "";

                    // フィード
                    if (!string.IsNullOrEmpty(xmlUrl))
                    {
                        total++;

                        string normalized = FeedUrlNormalizer.Normalize(xmlUrl);

                        if (existingUrls.Contains(normalized))
                        {
                            skipped++;
                            continue;
                        }

                        // ・サブフォルダ禁止 → ルート固定
                        string folder = "/";

                        var (feedId, isNew) = await _repository.AddFeedAsync(folder, title, normalized);

                        if (isNew)
                        {
                            added++;
                            existingUrls.Add(normalized);
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    // フォルダ
                    else if (outline.Elements("outline").Any())
                    {
                        // サブフォルダ禁止なので無視して中身だけ処理
                        await ProcessOutline(outline.Elements("outline"), currentPath);
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