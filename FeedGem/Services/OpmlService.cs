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
                    string title = (outline.Attribute("text")?.Value
                                ?? outline.Attribute("title")?.Value
                                ?? "無題").Trim();
                    string xmlUrl = outline.Attribute("xmlUrl")?.Value ?? "";

                    if (!string.IsNullOrEmpty(xmlUrl))
                    {
                        total++;
                        string normalized = FeedUrlNormalizer.Normalize(xmlUrl);
                        if (existingUrls.Contains(normalized)) { skipped++; continue; }

                        // 保存する際は、常に先頭にスラッシュを付けない形式で統一
                        string savePath = currentPath.Trim('/');
                        var (feedId, isNew) = await _repository.AddFeedAsync(savePath, title, normalized);

                        if (isNew) { added++; existingUrls.Add(normalized); }
                        else { skipped++; }
                    }
                    else
                    {
                        // 子要素がある場合はフォルダとして扱う
                        // 階層を深くする場合に備えて、パスを連結していく
                        string cleanTitle = title.Replace("/", "").Trim();
                        string nextPath = currentPath == "/" ? cleanTitle : $"{currentPath}/{cleanTitle}";

                        await ProcessOutline(outline.Elements("outline"), nextPath);
                    }
                }
            }

            return (total, added, skipped);
        }

        // OPMLエクスポート
        public async Task<XDocument> ExportAsync()
        {
            // 全フィードを取得
            var feeds = await _repository.GetAllFeedsAsync();

            // XMLのルート構造を作成
            var root = new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head", new XElement("title", "FeedGem Export")),
                new XElement("body"));

            var body = root.Element("body");
            if (body == null) return new XDocument(root);

            // フォルダパスの前後の空白やスラッシュを整理してグループ化
            // これにより、表記の揺れによるフォルダの重複を防ぐ
            var folders = feeds.GroupBy(f => f.FolderPath.Trim().Trim('/') switch
            {
                "" => "/", // 空文字やスラッシュのみはルートとして扱う
                var path => path
            });

            foreach (var folder in folders)
            {
                XContainer target = body;

                // ルート以外（特定のフォルダ名がある場合）はフォルダノードを作成
                if (folder.Key != "/")
                {
                    var folderNode = new XElement("outline",
                        new XAttribute("text", folder.Key)); // すでに正規化済みのためそのまま使用

                    body.Add(folderNode);
                    target = folderNode; // 以降のフィードはこのフォルダの中に追加する
                }

                foreach (var f in folder)
                {
                    // フォルダ自身を示す特殊なURLはエクスポートから除外
                    if (f.Url.StartsWith("folder://")) continue;

                    // フィード情報の追加
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