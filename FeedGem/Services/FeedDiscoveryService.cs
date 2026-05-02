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

        // フィード候補を探す（3段階: 直接取得 → HTML解析 → パス推測）
        public static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string url)
        {
            var candidates = new List<FeedCandidate>();
            var http = HttpClientProvider.Client;

            // 1. 入力されたURLそのものをフィードとして試す
            var direct = await TryLoadFeedAsync(http, url);
            if (direct != null)
            {
                candidates.Add(direct);
                return candidates;
            }

            // 2. HTML内の <link> タグからフィードURLを探す
            string? html = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                html = await http.GetStringAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                LoggingService.Error($"Discovery: Failed to retrieve HTML: {url}", ex);
            }

            if (html != null)
            {
                foreach (var candidate in ParseFeedLinksFromHtml(html, url))
                {
                    if (!candidates.Any(c => c.Url == candidate.Url))
                        candidates.Add(candidate);
                }
            }

            if (candidates.Count > 0)
                return candidates;

            // 3. よくあるパスを推測して試す
            Uri baseUri;
            try { baseUri = new Uri(url); }
            catch { return candidates; }

            foreach (var path in CommonPaths)
            {
                try
                {
                    var testUri = new Uri(baseUri, path);
                    var candidate = await TryLoadFeedAsync(http, testUri.AbsoluteUri);
                    if (candidate != null && !candidates.Any(c => c.Url == candidate.Url))
                        candidates.Add(candidate);
                }
                catch (Exception ex)
                {
                    LoggingService.Error($"Discovery: Failed to infer feed URL: {url}{path}", ex);
                }
            }

            return candidates;
        }

        // 指定URLをXMLとして読み込み、フィードとして有効なら FeedCandidate を返す
        private static async Task<FeedCandidate?> TryLoadFeedAsync(HttpClient http, string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var stream = await http.GetStreamAsync(url, cts.Token);

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                };

                using var reader = XmlReader.Create(stream, settings);

                // 最初の要素ノードまで読み進めてルート要素名を取得する
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element) break;
                }

                // rss / feed（Atom）/ RDF 以外はフィードとみなさない
                string rootName = reader.LocalName;
                if (rootName != "rss" && rootName != "feed" && rootName != "RDF")
                    return null;

                // ルート要素名でフォーマットを確定する
                string feedType = rootName == "feed" ? "Atom" : "RSS";

                // ルート要素の位置から SyndicationFeed を読み込む
                SyndicationFeed synFeed;
                try
                {
                    synFeed = SyndicationFeed.Load(reader);
                }
                catch (Exception ex)
                {
                    LoggingService.Error($"Discovery: Failed to load SyndicationFeed: {url}", ex);
                    return null;
                }

                string originalTitle = synFeed.Title?.Text?.Trim() ?? "Feed";
                string displayTitle = originalTitle;

                // タイトルにフォーマット名が含まれていない場合のみ付加する
                if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom"))
                    displayTitle += $" ({feedType})";

                return new FeedCandidate
                {
                    Title = displayTitle,
                    OriginalTitle = originalTitle,
                    Url = url,
                    Type = feedType
                };
            }
            catch (XmlException ex)
            {
                LoggingService.Info($"Discovery: Invalid XML format: {url} - {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LoggingService.Error($"Discovery: Failed to load feed: {url}", ex);
                return null;
            }
        }

        // HTMLソースの <link> タグを解析してフィード候補リストを返す
        private static List<FeedCandidate> ParseFeedLinksFromHtml(string html, string baseUrl)
        {
            var candidates = new List<FeedCandidate>();

            try
            {
                Uri baseUri = new(baseUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // ページタイトルをフォールバック用に取得する
                string pageTitle = doc.DocumentNode
                    .SelectSingleNode("//title")?.InnerText?.Trim() ?? "Unknown Feed";

                var nodes = doc.DocumentNode.SelectNodes("//link");
                if (nodes == null) return candidates;

                foreach (var node in nodes)
                {
                    string rel = node.GetAttributeValue("rel", "");
                    string typeAttr = node.GetAttributeValue("type", "");
                    string href = node.GetAttributeValue("href", "");
                    string title = System.Net.WebUtility.HtmlDecode(
                                          node.GetAttributeValue("title", ""));

                    if (!IsFeedLink(rel, typeAttr, title, href)) continue;

                    // コメント・トラックバック系フィードは除外する
                    if (href.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
                        href.Contains("trackback", StringComparison.OrdinalIgnoreCase) ||
                        href.Contains("comments", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string absoluteUrl = ResolveUrl(baseUri, href);
                    if (candidates.Any(c => c.Url == absoluteUrl)) continue;

                    string originalTitle = string.IsNullOrWhiteSpace(title) ? pageTitle : title;
                    string feedType = GetFeedType(typeAttr, absoluteUrl);
                    string displayTitle = originalTitle;

                    if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom")
                        && !string.IsNullOrEmpty(feedType))
                        displayTitle += $" ({feedType})";

                    candidates.Add(new FeedCandidate
                    {
                        Title = displayTitle,
                        OriginalTitle = originalTitle,
                        Url = absoluteUrl,
                        Type = feedType
                    });
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error($"Discovery: Failed to parse HTML: {baseUrl}", ex);
            }

            return candidates;
        }

        // <link> タグがフィードを指しているか判定する
        private static bool IsFeedLink(string rel, string typeAttr, string title, string href)
        {
            string relLower = rel.ToLowerInvariant();
            string typeLower = typeAttr.ToLowerInvariant();
            string titleLower = title.ToLowerInvariant();
            string hrefLower = href.ToLowerInvariant();

            // rel="alternate" を持つもののみ対象とする
            if (!relLower.Contains("alternate")) return false;

            if (typeLower.Contains("rss") || typeLower.Contains("atom") || typeLower.Contains("xml")) return true;
            if (titleLower.Contains("rss") || titleLower.Contains("atom") || titleLower.Contains("feed")) return true;
            if (hrefLower.Contains("rss") || hrefLower.Contains("atom") || hrefLower.Contains("feed")
                || hrefLower.EndsWith(".xml")) return true;

            return false;
        }

        // 相対URLを絶対URLに変換する
        private static string ResolveUrl(Uri baseUri, string href)
        {
            if (href.StartsWith("//"))
                return $"{baseUri.Scheme}:{href}";

            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            return new Uri(baseUri, href).AbsoluteUri;
        }

        // type属性またはURLからフォーマット（RSS / Atom）を判定する
        private static string GetFeedType(string typeAttrOrLocalName, string absoluteUrl)
        {
            string value = typeAttrOrLocalName?.ToLowerInvariant() ?? string.Empty;

            if (value.Contains("atom") || absoluteUrl.Contains("/atom", StringComparison.OrdinalIgnoreCase))
                return "Atom";

            if (value.Contains("rss") || absoluteUrl.Contains("/rss", StringComparison.OrdinalIgnoreCase)
                                       || absoluteUrl.Contains("/feed", StringComparison.OrdinalIgnoreCase))
                return "RSS";

            return "Unknown";
        }
    }
}