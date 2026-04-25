using System.Text.RegularExpressions;

namespace FeedGem.Services
{
    public static partial class FeedUrlNormalizer
    {
        [GeneratedRegex(@"^http://", RegexOptions.IgnoreCase)]
        private static partial Regex HttpRegex();

        // 許可するクエリパラメータキー（これ以外は除去）
        private static readonly HashSet<string> AllowedQueryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "xml",  // FC2: ?xml
            "all",  // FC2: &all
            "alt",  // Blogger系: ?alt=rss でフォーマット指定
        };

        // URL正規化とサイト別補正をまとめて行う
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            string result = url.Trim();

            // ・スキーム補完
            if (!result.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !result.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                result = "https://" + result;
            }

            // ・http → https 統一
            result = HttpRegex().Replace(result, "https://");

            if (!Uri.TryCreate(result, UriKind.Absolute, out var uri))
            {
                return result;
            }

            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = uri.IsDefaultPort ? -1 : uri.Port,
                Host = uri.Host.ToLowerInvariant()
            };

            result = ApplySiteSpecificRules(builder);
            result = FilterQuery(result);

            // ・末尾スラッシュ削除
            result = result.TrimEnd('/');

            return result;
        }

        // クエリパラメータをホワイトリストで選別する
        private static string FilterQuery(string url)
        {
            int qIndex = url.IndexOf('?');
            if (qIndex < 0)
                return url;

            string baseUrl = url[..qIndex];
            string queryPart = url[(qIndex + 1)..];

            // キーのみ（値なし）のパラメータも考慮して解析する
            var kept = queryPart
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(seg =>
                {
                    string key = seg.Split('=')[0];
                    return AllowedQueryKeys.Contains(key);
                })
                .ToList();

            return kept.Count > 0
                ? baseUrl + "?" + string.Join("&", kept)
                : baseUrl;
        }

        // サイト固有のURL補正を適用する
        private static string ApplySiteSpecificRules(UriBuilder builder)
        {
            string host = builder.Host.ToLowerInvariant();
            string path = builder.Path.TrimEnd('/');

            if (host.EndsWith("blog.fc2.com"))
            {
                builder.Path = "/";
                builder.Query = "xml";
                return builder.Uri.ToString();
            }

            if (host.Contains(".blogspot."))
            {
                if (!path.Contains("/feeds/posts", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Path = "/feeds/posts/default";
                    builder.Query = "";
                }

                return builder.Uri.ToString();
            }

            if (host.EndsWith(".hatenablog.com") || host.EndsWith(".hateblo.jp"))
            {
                if (!path.Contains("/feed", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("/rss", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Path = "/feed";
                    builder.Query = "";
                }

                return builder.Uri.ToString();
            }

            if (host.Contains("blog.livedoor.jp"))
            {
                if (!path.Contains("/index.rdf", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("/atom.xml", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Path = "/index.rdf";
                    builder.Query = "";
                }

                return builder.Uri.ToString();
            }

            return builder.Uri.ToString();
        }
    }
}