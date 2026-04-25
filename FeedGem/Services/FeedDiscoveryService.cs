using FeedGem.Models;
using HtmlAgilityPack;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Xml;

namespace FeedGem.Services
{
    public class FeedDiscoveryService
    {
        private static readonly string[] CommonPaths =
        [
            "/feed",
            "/feed/",
            "/rss",
            "/rss.xml",
            "/feed.xml",
            "/rss2.xml",
            "/atom.xml",
            "/index.xml",
            "/index.rdf",
            "/feeds/posts/default",
            "/feeds/posts/default?alt=rss",
            "/feeds/posts/default?alt=atom",
            "/releases.atom",
            "/commits.atom",
            "/atom"
        ];

        // フィード候補を探す
        public static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string url)
        {
            var candidates = new List<FeedCandidate>();
            var http = HttpClientProvider.Client;

            // 1. 入力されたURLそのものをフィードとして試す
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var stream = await http.GetStreamAsync(url, cts.Token);
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null
                };
                using var reader = XmlReader.Create(stream, settings);

                var feed = SyndicationFeed.Load(reader);

                if (feed != null)
                {
                    string type = "Unknown";

                    if (reader.LocalName == "feed")
                        type = "Atom";
                    else if (reader.LocalName == "rss")
                        type = "RSS";

                    candidates.Add(new FeedCandidate
                    {
                        Title = feed.Title?.Text ?? "Feed",
                        Url = url,
                        Type = type
                    });

                    return candidates;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error($"Discovery: 直接URL取得失敗: {url}", ex);
            }

            // 2. HTML内からフィードURLを探す
            try
            {
                
                var html = await http.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                Uri baseUri = new(url);
                var nodes = doc.DocumentNode.SelectNodes("//link");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string href = node.GetAttributeValue("href", "");
                        if (string.IsNullOrWhiteSpace(href))
                            continue;

                        string rel = node.GetAttributeValue("rel", "");
                        string typeAttr = node.GetAttributeValue("type", "");
                        string title = node.GetAttributeValue("title", "");
                        title = System.Net.WebUtility.HtmlDecode(title);

                        if (!IsFeedLink(rel, typeAttr, title, href))
                            continue;

                        // コメント系フィード除外
                        if (href.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
                            href.Contains("trackback", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string absoluteUrl = ResolveUrl(baseUri, href);

                        // 不要フィード除外
                        if (absoluteUrl.EndsWith("/rss", StringComparison.OrdinalIgnoreCase)
                         || absoluteUrl.EndsWith("/atom", StringComparison.OrdinalIgnoreCase)
                         || absoluteUrl.Contains("comments", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!candidates.Any(c => c.Url == absoluteUrl))
                        {
                            string originalTitle = title;
                            if (string.IsNullOrWhiteSpace(originalTitle))
                            {
                                originalTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "不明なフィード";
                            }

                            string feedType = GetFeedType(typeAttr, absoluteUrl);
                            string displayTitle = originalTitle;

                            if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom") && !string.IsNullOrEmpty(feedType))
                            {
                                displayTitle += $" ({feedType})";
                            }

                            candidates.Add(new FeedCandidate
                            {
                                Title = displayTitle,
                                OriginalTitle = originalTitle,
                                Url = absoluteUrl,
                                Type = feedType
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error($"Discovery: HTML解析失敗: {url}", ex);
            }

            // 3. よくあるフィードURLを推測して試す
            foreach (var path in CommonPaths)
            {
                Uri? testUri = null;

                try
                {
                    Uri baseUri = new(url);
                    testUri = new(baseUri, path);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var stream = await http.GetStreamAsync(testUri.AbsoluteUri, cts.Token);
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Ignore,
                        XmlResolver = null
                    };
                    using var reader = XmlReader.Create(stream, settings);

                    var feed = SyndicationFeed.Load(reader);

                    if (feed != null && !candidates.Any(c => c.Url == testUri.AbsoluteUri))
                    {
                        string feedType = GetFeedType(reader.LocalName, testUri.AbsoluteUri);

                        string originalTitle = System.Net.WebUtility.HtmlDecode(feed.Title?.Text ?? "Feed");
                        string displayTitle = originalTitle;

                        if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom") && !string.IsNullOrEmpty(feedType))
                        {
                            displayTitle += $" ({feedType})";
                        }

                        candidates.Add(new FeedCandidate
                        {
                            Title = displayTitle,
                            OriginalTitle = originalTitle,
                            Url = testUri.AbsoluteUri,
                            Type = feedType
                        });
                    }
                }
                catch (Exception ex)
                {
                    string failedUrl = testUri?.AbsoluteUri ?? $"{url}{path}";
                    LoggingService.Error($"Discovery: 推測URL失敗: {failedUrl}", ex);
                }
            }
            return candidates;
        }

        private static bool IsFeedLink(string rel, string typeAttr, string title, string href)
        {
            string relLower = rel.ToLowerInvariant();
            string typeLower = typeAttr.ToLowerInvariant();
            string titleLower = title.ToLowerInvariant();
            string hrefLower = href.ToLowerInvariant();

            if (!relLower.Contains("alternate"))
                return false;

            if (typeLower.Contains("rss") || typeLower.Contains("atom") || typeLower.Contains("xml"))
                return true;

            if (titleLower.Contains("rss") || titleLower.Contains("atom") || titleLower.Contains("feed"))
                return true;

            if (hrefLower.Contains("rss") || hrefLower.Contains("atom") || hrefLower.Contains("feed") || hrefLower.EndsWith(".xml"))
                return true;

            return false;
        }

        private static string ResolveUrl(Uri baseUri, string href)
        {
            if (href.StartsWith("//"))
                return $"{baseUri.Scheme}:{href}";

            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            return new Uri(baseUri, href).AbsoluteUri;
        }

        private static string GetFeedType(string typeAttrOrLocalName, string absoluteUrl)
        {
            string value = typeAttrOrLocalName?.ToLowerInvariant() ?? string.Empty;

            if (value.Contains("atom") || absoluteUrl.Contains("/atom", StringComparison.OrdinalIgnoreCase))
                return "Atom";

            if (value.Contains("rss") || absoluteUrl.Contains("/rss", StringComparison.OrdinalIgnoreCase) || absoluteUrl.Contains("/feed", StringComparison.OrdinalIgnoreCase))
                return "RSS";

            return "Unknown";
        }
    }
}