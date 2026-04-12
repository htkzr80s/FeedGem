using FeedGem.Models;
using HtmlAgilityPack;
using System.ServiceModel.Syndication;
using System.Xml;

namespace FeedGem.Services
{
    public class FeedDiscoveryService
    {
        // フィード候補を探す
        public static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string url)
        {
            var candidates = new List<FeedCandidate>();

            // 1. 入力されたURLそのものをフィードとして試す
            try
            {
                var http = HttpClientProvider.Client;
                http.Timeout = TimeSpan.FromSeconds(10);

                var stream = await http.GetStreamAsync(url);
                using var reader = XmlReader.Create(stream);

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
                        Title = feed.Title?.Text ?? "フィード",
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
                var web = new HtmlWeb();
                var doc = await web.LoadFromWebAsync(url);

                // RSS/Atomを示唆するlinkタグを広めに探す
                var nodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate' or @type='application/rss+xml' or @type='application/atom+xml']");

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        string href = node.GetAttributeValue("href", "");
                        string typeAttr = node.GetAttributeValue("type", "").ToLower();
                        string title = node.GetAttributeValue("title", "");
                        // HTMLデコード（&raquo; → »）
                        title = System.Net.WebUtility.HtmlDecode(title);

                        if (string.IsNullOrEmpty(href)) continue;

                        // コメント系フィード除外
                        if (href.Contains("comment") || href.Contains("trackback")) continue;

                        // 相対 → 絶対URL
                        Uri baseUri = new(url);
                        Uri fullUri = new(baseUri, href);
                        string absoluteUrl = fullUri.AbsoluteUri;

                        // 不要フィード除外
                        if (absoluteUrl.EndsWith("/rss")
                         || absoluteUrl.EndsWith("/atom")
                         || absoluteUrl.Contains("comments"))
                            continue;

                        // タイトル補完
                        if (string.IsNullOrWhiteSpace(title) || title.Equals("RSS", StringComparison.CurrentCultureIgnoreCase))
                        {
                            title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "不明なフィード";
                        }

                        // 種別判定
                        string feedType = "Unknown";

                        if (typeAttr.Contains("atom"))
                            feedType = "Atom";
                        else if (typeAttr.Contains("rss"))
                            feedType = "RSS";

                        // 重複チェック
                        if (!candidates.Any(c => c.Url == absoluteUrl))
                        {
                            string originalTitle = title;
                            // UI表示用
                            string displayTitle = originalTitle;

                            // 種別がタイトルに含まれていなければ付ける
                            if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom"))
                            {
                                displayTitle += $" ({feedType})";
                            }

                            candidates.Add(new FeedCandidate
                            {
                                Title = displayTitle,        // UI表示用
                                OriginalTitle = originalTitle, // ← 追加
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
            var commonPaths = new[]
            {
                "/feed",
                "/rss",
                "/rss.xml",
                "/atom.xml",
                "/index.xml",
                "/feeds/posts/default",
                "/releases.atom",
                "/commits.atom",
                "/atom"
            };

            foreach (var path in commonPaths)
            {
                Uri? testUri = null; // ← ここで1回だけ宣言

                try
                {
                    Uri baseUri = new(url);
                    testUri = new(baseUri, path); // ← 宣言じゃなく代入にする

                    var http = HttpClientProvider.Client;

                    var stream = await http.GetStreamAsync(testUri.AbsoluteUri);
                    using var reader = XmlReader.Create(stream);

                    var feed = SyndicationFeed.Load(reader);

                    if (feed != null && !candidates.Any(c => c.Url == testUri.AbsoluteUri))
                    {
                        string feedType = "Unknown";

                        // URLベースで補助判定
                        string lowerUrl = testUri.AbsoluteUri.ToLower();

                        if (lowerUrl.Contains("atom"))
                            feedType = "Atom";
                        else if (lowerUrl.Contains("rss") || lowerUrl.Contains("feed"))
                            feedType = "RSS";

                        // 元タイトル取得
                        string originalTitle = System.Net.WebUtility.HtmlDecode(feed.Title?.Text ?? "フィード");

                        string displayTitle = originalTitle;

                        if (!displayTitle.Contains("RSS") && !displayTitle.Contains("Atom"))
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
    }
}