using FeedGem.Models;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
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

                var stream = await http.GetStreamAsync(url);
                using var reader = XmlReader.Create(stream);

                var feed = SyndicationFeed.Load(reader);

                if (feed != null)
                {
                    candidates.Add(new FeedCandidate
                    {
                        Title = feed.Title?.Text ?? "フィード",
                        Url = url
                    });
                    return candidates;
                }
            }
            catch { }

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
                        string type = node.GetAttributeValue("type", "").ToLower();
                        string title = node.GetAttributeValue("title", "");

                        if (string.IsNullOrEmpty(href)) continue;

                        // 記事そのもののリンクや、コメント用フィードなどを除外するフィルタ
                        if (href.Contains("comment") || href.Contains("trackback")) continue;

                        // 相対パス（/feed等）を絶対パス（https://.../feed）に変換
                        Uri baseUri = new(url);
                        Uri fullUri = new(baseUri, href);
                        string absoluteUrl = fullUri.AbsoluteUri;

                        // タイトルが空ならサイトの<title>を借りる
                        if (string.IsNullOrWhiteSpace(title) || title.Equals("RSS", StringComparison.CurrentCultureIgnoreCase))
                        {
                            title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "不明なフィード";
                        }

                        // 重複チェックをして追加
                        if (!candidates.Any(c => c.Url == absoluteUrl))
                        {
                            candidates.Add(new FeedCandidate { Title = title, Url = absoluteUrl });
                        }
                    }
                }
            }
            catch { /* 次のHTML解析へ */ }

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
                try
                {
                    Uri baseUri = new(url);
                    Uri testUri = new(baseUri, path);

                    var http = HttpClientProvider.Client;

                    var stream = await http.GetStreamAsync(testUri.AbsoluteUri);
                    using var reader = XmlReader.Create(stream);

                    var feed = SyndicationFeed.Load(reader);

                    if (feed != null && !candidates.Any(c => c.Url == testUri.AbsoluteUri))
                    {
                        candidates.Add(new FeedCandidate
                        {
                            Title = feed.Title?.Text ?? "フィード",
                            Url = testUri.AbsoluteUri
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"探索エラー: {ex.Message}");
                }
            }
            return candidates;
        }
    }
}