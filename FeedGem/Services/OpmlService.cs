using FeedGem.Data;
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
            async Task ProcessOutlineSemiFlattened(IEnumerable<XElement> elements, string parentPath = "/")
            {
                foreach (var outline in elements)
                {
                    // textまたはtitle属性から表示名を取得
                    string title = (outline.Attribute("text")?.Value
                                ?? outline.Attribute("title")?.Value
                                ?? "無題").Trim();

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
                            await _repository.AddFeedAsync(parentPath, title, normalized);
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
                        if (parentPath == "/")
                        {
                            // ルート階層なら新しいフォルダを作成
                            string uniqueName = await _repository.GetUniqueFolderNameAsync(title);

                            // フォルダを識別するための擬似URLを生成して登録
                            await _repository.AddFeedAsync("/", uniqueName, "folder://" + Guid.NewGuid());

                            // 子要素をそのフォルダ配下として処理
                            await ProcessOutlineSemiFlattened(outline.Elements("outline"), "/" + uniqueName);
                        }
                        else
                        {
                            // 既にサブフォルダ内なら、階層を深くせず現在のフォルダに子要素を展開
                            await ProcessOutlineSemiFlattened(outline.Elements("outline"), parentPath);
                        }
                    }
                }
            }

            // 初回呼び出し
            await ProcessOutlineSemiFlattened(body.Elements("outline"), "/");

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