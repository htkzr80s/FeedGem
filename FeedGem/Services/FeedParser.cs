using FeedGem.Models;
using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace FeedGem.Services
{
    public static partial class FeedParser
    {
        [GeneratedRegex("<[^>]*>")]
        private static partial Regex HtmlTagRegex();

        // RSSまたはAtomフィードのストリームを解析し、記事リストを返す
        public static List<ArticleItem> Parse(Stream stream)
        {
            XDocument doc;

            // XML読み込みに失敗した場合は空リストを返す
            try
            {
                doc = XDocument.Load(stream);
            }
            catch
            {
                return [];
            }

            var root = doc.Root;
            if (root == null) return [];

            List<ArticleItem> results;

            // Atom 1.0 の判定
            if (root.Name.LocalName == "feed")
                results = [.. ParseAtom(doc).Where(a =>
            !string.IsNullOrWhiteSpace(a.Title) &&
            !string.IsNullOrWhiteSpace(a.Url))];

            // RSS 2.0 / RSS 1.0(RDF) の判定
            else if (root.Name.LocalName == "rss" || root.Name.LocalName == "RDF")
                results = [.. ParseRss(doc).Where(a =>
            !string.IsNullOrWhiteSpace(a.Title) &&
            !string.IsNullOrWhiteSpace(a.Url))];

            else
                return [];

            // フィード側の並び順に依存しないよう、日付の降順（新しい順）で並び替える
            return [.. results.OrderByDescending(a => a.Date)];
        }

        // Atom 1.0 形式を解析する
        private static List<ArticleItem> ParseAtom(XDocument doc)
        {
            XNamespace ns = "http://www.w3.org/2005/Atom";
            var root = doc.Root!;

            string feedTitle = root.Element(ns + "title")?.Value ?? "";

            var articles = new List<ArticleItem>();

            foreach (var entry in root.Elements(ns + "entry"))
            {
                string title = entry.Element(ns + "title")?.Value ?? "";

                // rel="alternate" を優先し、なければ最初の link を使用する
                string url = entry.Elements(ns + "link")
                    .FirstOrDefault(e => e.Attribute("rel")?.Value == "alternate")
                    ?.Attribute("href")?.Value
                    ?? entry.Element(ns + "link")?.Attribute("href")?.Value
                    ?? "";

                // summary → content の順で取得する
                string summary = entry.Element(ns + "summary")?.Value
                    ?? entry.Element(ns + "content")?.Value
                    ?? "";

                // published を優先し、なければ updated を使用する
                string? dateStr = entry.Element(ns + "published")?.Value
                    ?? entry.Element(ns + "updated")?.Value;

                articles.Add(new ArticleItem
                {
                    Title = title.Trim(),
                    Url = url.Trim(),
                    Summary = StripHtml(summary).Trim(),
                    Date = ParseDate(dateStr),
                    FeedTitle = feedTitle.Trim()
                });
            }

            return articles;
        }

        // RSS 2.0 / RSS 1.0(RDF) 形式を解析する
        private static List<ArticleItem> ParseRss(XDocument doc)
        {
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace content = "http://purl.org/rss/1.0/modules/content/";

            // RSS 1.0(RDF) と 2.0 の両方に対応するため LocalName で検索する
            var channel = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "channel");
            string feedTitle = channel?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";

            var articles = new List<ArticleItem>();

            foreach (var item in doc.Descendants().Where(e => e.Name.LocalName == "item"))
            {
                string title = item.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";

                string url = item.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "link")?.Value ?? "";

                // content:encoded → description の順で取得する
                string summary = item.Element(content + "encoded")?.Value
                    ?? item.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "description")?.Value
                    ?? "";

                // pubDate → dc:date の順で取得する
                string? dateStr = item.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "pubDate")?.Value
                    ?? item.Element(dc + "date")?.Value;

                articles.Add(new ArticleItem
                {
                    Title = title.Trim(),
                    Url = url.Trim(),
                    Summary = StripHtml(summary).Trim(),
                    Date = ParseDate(dateStr),
                    FeedTitle = feedTitle.Trim()
                });
            }

            return articles;
        }

        // 日付文字列を DateTime に変換する（解析失敗時は現在時刻を返す）
        private static DateTime ParseDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now;

            // ISO 8601 などの一般的な形式を試みる
            if (DateTime.TryParse(dateStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
            {
                return dt.ToLocalTime();
            }

            // RFC 822 形式（例: Mon, 02 Jan 2006 15:04:05 +0900）を試みる
            string[] rfc822Formats =
           [
                "ddd, dd MMM yyyy HH:mm:ss zzz",
                "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
                "ddd, dd MMM yyyy HH:mm:ss 'UTC'",
                "dd MMM yyyy HH:mm:ss zzz"
           ];

            if (DateTimeOffset.TryParseExact(
                dateStr,
                rfc822Formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dto))
            {
                return dto.LocalDateTime;
            }

            // ・末尾が "UT" のRFC 822 非標準形式（SourceForge等）を "UTC" に補正して再試行する
            if (dateStr.EndsWith(" UT", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTimeOffset.TryParseExact(
                    dateStr[..^2] + "UTC",
                    rfc822Formats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var dtoUt))
                {
                    return dtoUt.LocalDateTime;
                }
            }

            return DateTime.Now;
        }

        // HTMLタグを除去し、プレーンテキストを返す
        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            return HtmlTagRegex().Replace(html, " ")
                .Replace("&nbsp;", " ")
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Trim();
        }
    }
}