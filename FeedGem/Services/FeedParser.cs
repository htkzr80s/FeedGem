using FeedGem.Models;
using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;
using System.Xml.Linq;

namespace FeedGem.Services
{
    public static class FeedParser
    {
        // URL解決（relative → absolute）
        private static string ResolveUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrEmpty(relativeUrl))
                return baseUrl;

            // すでに絶対URLならそのまま
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (Uri.TryCreate(new Uri(baseUrl), relativeUrl, out var resolved))
                return resolved.ToString();

            return relativeUrl;
        }

        // フィード解析（RSS / Atom / RDF）
        public static List<ArticleItem> Parse(Stream stream, string baseUrl)
        {
            // ストリームをメモリにコピー
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            // --- RSS / Atom を先に試す ---
            try
            {
                ms.Position = 0;

                using var reader = XmlReader.Create(ms);
                var feed = SyndicationFeed.Load(reader);

                // アイテムが取得できた場合のみ採用する
                if (feed != null && feed.Items.Any())
                {
                    return [.. feed.Items.Select(item => ParseItem(item, baseUrl))];
                }
            }
            catch
            {
                // RSS/Atom解析失敗は無視してRDFへ進む
            }

            // --- RDF を試す（必ず実行される） ---
            ms.Position = 0;
            return ParseRdf(ms, baseUrl);
        }

        // RSS / Atom の1記事解析
        private static ArticleItem ParseItem(SyndicationItem item, string baseUrl)
        {
            string title = item.Title?.Text ?? "";

            // 元URL取得
            string rawLink = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

            string link = ResolveUrl(baseUrl, rawLink);

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
        private static List<ArticleItem> ParseRdf(Stream stream, string baseUrl)
        {
            var list = new List<ArticleItem>();

            if (stream.CanSeek)
                stream.Position = 0;

            var doc = XDocument.Load(stream);

            // 名前空間に依存せず item を取得する
            var items = doc.Descendants()
                           .Where(e => e.Name.LocalName == "item");

            foreach (var node in items)
            {
                // タイトル
                string title = node.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";

                // リンク
                string rawLink = node.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "link")?.Value ?? "";

                string link = ResolveUrl(baseUrl, rawLink);

                // 説明
                string desc = node.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "description")?.Value ?? "";

                if (string.IsNullOrEmpty(desc))
                {
                    desc = $"<a href='{link}'>記事を開く</a>";
                }

                // 日付（dc:date対応）
                string dateVal = node.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "date")?.Value ?? "";

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