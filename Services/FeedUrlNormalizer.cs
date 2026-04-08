using System;

namespace FeedGem.Services
{
    public static class FeedUrlNormalizer
    {
        // URL正規化とサイト別補正をまとめて行う
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            string result = url.Trim();

            // スキーム補完
            if (!result.StartsWith("http://") && !result.StartsWith("https://"))
            {
                result = "https://" + result;
            }

            // --- サイト別補正 ---
            result = ApplySiteSpecificRules(result);

            return result;
        }

        // サイト固有のURL補正を適用する
        private static string ApplySiteSpecificRules(string url)
        {
            // FC2ブログ対応
            if (url.Contains("blog.fc2.com"))
            {
                // ?xml がない場合追加
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