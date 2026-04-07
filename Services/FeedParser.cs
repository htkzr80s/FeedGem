using FeedGem.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            try
            {
                // --- RSS / Atom ---
                using var reader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(reader);

                if (feed != null)
                {
                    return [.. feed.Items.Select(ParseItem)];
                }
            }
            catch
            {
                // 無視してRDFへ
            }

            // --- RDF (FC2など) ---
            return ParseRdf(stream);
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

            var doc = XDocument.Load(stream);

            XNamespace ns = "http://purl.org/rss/1.0/";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";

            var items = doc.Descendants(ns + "item");

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