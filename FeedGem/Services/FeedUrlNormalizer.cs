using System.Text.RegularExpressions;

namespace FeedGem.Services
{
    public static partial class FeedUrlNormalizer
    {
        [GeneratedRegex(@"^http://", RegexOptions.IgnoreCase)]
        private static partial Regex HttpRegex();

        // URL正規化とサイト別補正をまとめて行う
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            string result = url.Trim();

            // ・スキーム補完
            if (!result.StartsWith("http://") && !result.StartsWith("https://"))
            {
                result = "https://" + result;
            }

            // ・http → https 統一
            result = HttpRegex().Replace(result, "https://");

            // ・小文字化（比較安定）
            result = result.ToLowerInvariant();

            // ・クエリ除去（基本）
            // ※ただし後で必要なものは復活させる
            int qIndex = result.IndexOf('?');
            if (qIndex > 0)
            {
                result = result[..qIndex];
            }

            // ・末尾スラッシュ削除
            result = result.TrimEnd('/');

            // ・サイト別補正
            result = ApplySiteSpecificRules(result);

            return result;
        }

        // サイト固有のURL補正を適用する
        private static string ApplySiteSpecificRules(string url)
        {
            // FC2ブログ対応
            if (url.Contains("blog.fc2.com"))
            {
                // ?xml を付与
                if (!url.Contains("?xml"))
                {
                    url = url.TrimEnd('/') + "/?xml";
                }

                // 全記事取得
                if (!url.Contains("&all"))
                {
                    url += "&all";
                }
            }

            return url;
        }
    }
}