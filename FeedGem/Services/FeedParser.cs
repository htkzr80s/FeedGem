using FeedGem.Models;
using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using System.Xml.Linq;

namespace FeedGem.Services
{
    public static class FeedParser
    {
        // フィード解析（RSS / Atom / RDF）
        public static List<ArticleItem> Parse(Stream stream)
        {
            // --- ストリームをメモリにコピー ---
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            // --- 1回目：RSS / Atom ---
            try
            {
                ms.Position = 0;
                using var reader = XmlReader.Create(ms);
                var feed = SyndicationFeed.Load(reader);

                if (feed != null && feed.Items.Any())
                {
                    return [.. feed.Items.Select(ParseItem)];
                }
            }
            catch
            {
                // 無視してRDFへ
            }

            // --- 2回目：RDF ---
            ms.Position = 0;

            // XMLかどうか + フィードかどうかチェック
            try
            {
                ms.Position = 0;

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit // HTMLのDOCTYPE対策
                };

                using var testReader = XmlReader.Create(ms, settings);

                // ルート要素まで読む
                testReader.MoveToContent();

                string rootName = testReader.Name.ToLower();

                // RSS / Atom / RDF 以外は弾く
                if (rootName != "rss" && rootName != "feed" && rootName != "rdf")
                {
                    return [];
                }
            }
            catch
            {
                // XMLですらない（HTMLなど）
                return [];
            }

            // 位置戻す
            ms.Position = 0;
            return ParseRdf(ms);
        }

        // RSS / Atom の1記事解析
        private static ArticleItem ParseItem(SyndicationItem item)
        {
            string title = item.Title?.Text ?? "";
            string link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

            // 本文取得
            string summary = "";

            var encodedExt = item.ElementExtensions
                .FirstOrDefault(e => e.OuterName == "encoded" || e.OuterName == "content");

            if (encodedExt != null)
            {
                try
                {
                    using var readerExt = encodedExt.GetReader();
                    var element = XElement.Load(readerExt);
                    summary = element.Value;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(summary) && item.Content is TextSyndicationContent content)
            {
                summary = content.Text;
            }

            if (string.IsNullOrEmpty(summary))
            {
                summary = item.Summary?.Text ?? "";
            }

            if (string.IsNullOrEmpty(summary))
            {
                summary = $"<a href='{link}'>記事を開く</a>";
            }

            // 日付
            DateTime pubDate =
                item.PublishDate != default ? item.PublishDate.DateTime :
                item.LastUpdatedTime != default ? item.LastUpdatedTime.DateTime :
                DateTime.Now;

            return new ArticleItem
            {
                Title = title,
                Url = link,
                Summary = summary,
                Date = pubDate
            };
        }

        // RDF解析
        private static List<ArticleItem> ParseRdf(Stream stream)
        {
            var list = new List<ArticleItem>();

            // ストリームの読み取り位置を先頭に戻す
            if (stream.CanSeek) stream.Position = 0;

            var doc = XDocument.Load(stream);
            XNamespace ns = "http://purl.org/rss/1.0/";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";

            // channel内のitems/Seqではなく、ルート直下のitem要素を直接取得する
            var items = doc.Root?.Elements(ns + "item") ?? doc.Descendants(ns + "item");

            foreach (var node in items)
            {
                string title = node.Element(ns + "title")?.Value ?? "";
                string link = node.Element(ns + "link")?.Value ?? "";
                string desc = node.Element(ns + "description")?.Value ?? "";

                if (string.IsNullOrEmpty(desc))
                {
                    desc = $"<a href='{link}'>記事を開く</a>";
                }

                string dateVal = node.Element(dc + "date")?.Value ?? "";

                DateTime published = DateTimeOffset.TryParse(dateVal, out var parsed)
                    ? parsed.DateTime
                    : DateTime.Now;

                list.Add(new ArticleItem
                {
                    Title = title,
                    Url = link,
                    Summary = desc,
                    Date = published
                });
            }

            return list;
        }
    }
}