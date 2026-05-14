using FeedGem.Core;
using FeedGem.Models;
using HtmlAgilityPack;
using System.Net.Http;
using System.Xml;

namespace FeedGem.Services
{
    public static class FeedDiscoveryService
    {
        private static readonly string[] CommonPaths =
                [
                    "/feed", "/feed/", "/rss", "/rss.xml", "/feed.xml", "/rss2.xml",
                    "/atom.xml", "/index.xml", "/index.rdf", "/feeds/posts/default",
                    "/feeds/posts/default?alt=rss", "/feeds/posts/default?alt=atom",
                    "/releases.atom", "/commits.atom", "/atom"
                ];

        // フィード候補を探す（3段階: 直接取得 → HTML解析 → パス推測）
        public static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string url)
        {
            var candidates = new List<FeedCandidate>();
            var http = HttpClientProvider.Client;
            var rng = new Random();

            // HTTPで入力された場合でも、安全なHTTPSでの試行を優先する
            string secureUrl = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? "https://" + url[7..]
                : url;

            // 1. 入力URLを直接検証
            var direct = await TryLoadFeedAsync(http, secureUrl);

            // 入力URL自体が有効なフィードなら、まず候補に追加する
            if (direct != null)
            {
                candidates.Add(direct);

                // 4Gamerの例のように、明らかに「目次」のようなRSSページである場合、
                // さらにその中のリンクを掘り下げる必要があるため、ここでは return せずに続行する
            }

            // 2. HTML内の <link> タグから探す
            string? html = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await http.GetAsync(secureUrl, cts.Token);

                // 403 Forbidden が出た場合は、それ以上の探索（パス推測）を中止する
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    LoggingService.DebugOnly($"Discovery: 403 Forbidden at {secureUrl}. Stopping further discovery.");
                    return candidates;
                }

                if (response.IsSuccessStatusCode)
                {
                    html = await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                LoggingService.DebugOnly($"Discovery: Primary HTML fetch failed: {ex.Message}");
            }

            if (html != null)
            {
                foreach (var candidate in ParseFeedLinksFromHtml(html, secureUrl))
                {
                    if (candidates.Count >= AppConstants.MaxCandidateCount) break;
                    if (!candidates.Any(c => NormalizeUrl(c.Url) == NormalizeUrl(candidate.Url)))
                        candidates.Add(candidate);
                }
            }

            if (candidates.Count >= AppConstants.MaxCandidateCount) return candidates;

            // 3. パス推測フェーズ（負荷を考慮し、少し待機を入れる）
            if (!Uri.TryCreate(secureUrl, UriKind.Absolute, out var baseUri)) return candidates;

            foreach (var path in CommonPaths)
            {
                if (candidates.Count >= AppConstants.MaxCandidateCount) break;

                // 連続アクセスによるBAN・bot判定を避けるため、間隔をランダムにする
                int delayMs = rng.Next(1000, 2000);
                await Task.Delay(delayMs);

                try
                {
                    var testUri = new Uri(baseUri, path);
                    var candidate = await TryLoadFeedAsync(http, testUri.AbsoluteUri);

                    if (candidate != null && !candidates.Any(c => NormalizeUrl(c.Url) == NormalizeUrl(candidate.Url)))
                        candidates.Add(candidate);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // 403 が返ってきたらこのサーバーへの推測アクセスを即座に中止する
                    LoggingService.DebugOnly($"Discovery: 403 during path probing. Stopping. ({baseUri.Host})");
                    break;
                }
            }

            return candidates;
        }

        // 指定URLをXMLとして読み込み検証する
        private static async Task<FeedCandidate?> TryLoadFeedAsync(HttpClient http, string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                // GetStreamAsyncではなくGetAsyncを使い、ステータスコードをチェックできるようにする
                var response = await http.GetAsync(url, cts.Token);

                // 403 が返ってきた場合は、呼び出し元に検知させるため例外を投げる
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new HttpRequestException("403 Forbidden", null, System.Net.HttpStatusCode.Forbidden);
                }

                if (!response.IsSuccessStatusCode) return null;

                using var stream = await response.Content.ReadAsStreamAsync();
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(stream, settings);

                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    string rootName = reader.LocalName.ToLowerInvariant();

                    if (rootName is "rss" or "feed" or "rdf")
                    {
                        string feedType = rootName == "feed" ? "Atom" : "RSS";
                        return new FeedCandidate
                        {
                            Title = $"{url} ({feedType})",
                            OriginalTitle = url,
                            Url = url,
                            Type = feedType
                        };
                    }
                    return null;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // 403 は再スローして呼び出し元に伝える
                throw;
            }
            catch (Exception ex)
            {
                LoggingService.DebugOnly($"Discovery: TryLoadFeed failed: {url} - {ex.Message}");
            }
            return null;
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
                    // 設定された最大件数を超えたら解析を中止
                    if (candidates.Count >= AppConstants.MaxCandidateCount) break;

                    string rel = node.GetAttributeValue("rel", "");
                    string typeAttr = node.GetAttributeValue("type", "");
                    string href = node.GetAttributeValue("href", "");
                    string title = System.Net.WebUtility.HtmlDecode(node.GetAttributeValue("title", ""));

                    // フィードを指すリンクか判定
                    if (!IsFeedLink(rel, typeAttr, title, href)) continue;

                    // 不要なコメント用フィード等は除外
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

        // linkタグの属性からフィードリンクか判断する
        private static bool IsFeedLink(string rel, string typeAttr, string title, string href)
        {
            string relLower = rel.ToLowerInvariant();
            string typeLower = typeAttr.ToLowerInvariant();
            string titleLower = title.ToLowerInvariant();
            string hrefLower = href.ToLowerInvariant();

            // rel属性にalternateが含まれていることを必須条件とする
            if (!relLower.Contains("alternate")) return false;

            // 属性値からフィードに関連するキーワードが含まれているか判定
            if (typeLower.Contains("rss") || typeLower.Contains("atom") || typeLower.Contains("xml")) return true;
            if (titleLower.Contains("rss") || titleLower.Contains("atom") || titleLower.Contains("feed")) return true;
            if (hrefLower.Contains("rss") || hrefLower.Contains("atom") || hrefLower.Contains("/feed") || hrefLower.EndsWith(".xml")) return true;

            return false;
        }

        // URLの微細な違い（http/https、末尾の/）を無視して比較するための正規化メソッド
        private static string NormalizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url.ToLowerInvariant());
                // ホスト名とパスのみを抽出して比較のキーとする
                string normalized = uri.Host + uri.AbsolutePath;
                return normalized.TrimEnd('/');
            }
            catch
            {
                // 解析不能なURLは小文字化と末尾削除のみ行う
                return url.ToLowerInvariant().TrimEnd('/');
            }
        }

        // 相対URLを絶対URLに変換し、必要に応じてHTTPSに昇格させるメソッド
        private static string ResolveUrl(Uri baseUri, string href)
        {
            string resolved;

            // スキーム省略（//）から始まるURLへの対応
            if (href.StartsWith("//"))
            {
                resolved = $"{baseUri.Scheme}:{href}";
            }
            // 絶対URLとして解釈可能な場合
            else if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            {
                resolved = absolute.ToString();
            }
            // それ以外は相対パスとして結合
            else
            {
                resolved = new Uri(baseUri, href).AbsoluteUri;
            }

            // ベースがHTTPSなら、解決されたURLもHTTPSに強制アップグレードする
            if (baseUri.Scheme == "https" && resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                resolved = "https://" + resolved[7..];
            }

            return resolved;
        }

        // URLやtype属性からRSSかAtomかを特定する
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

        // 指定されたURLがセキュリティ設定（AllowInsecureHttp）に違反していないか検証する
        public static bool IsUrlSecurityAllowed(string url)
        {
            // URLが http:// で始まっているか判定（大文字小文字を区別しない）
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                // 設定で非暗号化通信が許可されていない場合は false を返す
                if (!AppSettings.AllowInsecureHttp)
                {
                    return false;
                }
            }

            // https:// の場合、または http:// でも設定で許可されている場合は true を返す
            return true;
        }
    }
}