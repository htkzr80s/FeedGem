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

            // ・小文字化（比較安定）
            result = result.ToLowerInvariant();

            // ・サイト別補正（クエリ除去より先に行う）
            result = ApplySiteSpecificRules(result);

            // ・クエリの選別（許可リスト以外を除去）
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
        private static string ApplySiteSpecificRules(string url)
        {
            // FC2ブログ対応
            if (url.Contains("blog.fc2.com"))
            {
                // ?xml のみ付与（これだけで十分）
                if (!url.Contains("?xml"))
                    url = url.TrimEnd('/') + "/?xml";

                // &all は付与しない（不安定要因になるため）
            }

            // Blogger / Blogspot 対応（Python Insiderなど）
            // 例: pythoninsider.blogspot.com → フィードパスに変換する
            else if (url.Contains(".blogspot."))
            {
                url = NormalizeBlogspot(url);
            }

            // はてなブログ対応
            // 例: xxx.hatenablog.com → /feed を付与する
            else if (url.Contains(".hatenablog.com") ||
                     url.Contains(".hateblo.jp"))
            {
                if (!url.Contains("/feed") && !url.Contains("/rss"))
                    url = url.TrimEnd('/') + "/feed";
            }

            // livedoor ブログ対応
            else if (url.Contains("blog.livedoor.jp"))
            {
                if (!url.Contains("/index.rdf") && !url.Contains("/atom.xml"))
                    url = url.TrimEnd('/') + "/index.rdf";
            }

            return url;
        }

        // Blogspotのトップ/記事URLをAtomフィードURLに変換する
        private static string NormalizeBlogspot(string url)
        {
            // すでにフィードパスなら何もしない
            if (url.Contains("/feeds/posts"))
                return url;

            // 記事URLのパスを除いてトップに戻す
            // 例: pythoninsider.blogspot.com/2024/01/xxx.html
            //   → pythoninsider.blogspot.com
            var uri = new Uri(url);
            string root = uri.Scheme + "://" + uri.Host;

            // Blogger標準のAtomフィードパスを付与する
            return root + "/feeds/posts/default";
        }
    }
}