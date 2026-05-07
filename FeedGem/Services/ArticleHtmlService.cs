using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using static FeedGem.Services.LocalizationService;


namespace FeedGem.Services
{
    public static partial class ArticleHtmlService
    {
        // scriptタグ削除用
        [GeneratedRegex("<script[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex ScriptTagRegex();

        // FC2フッター削除用
        [GeneratedRegex("<div class=\"fc2_footer\"[^>]*>.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex Fc2FooterRegex();

        // 記事の内容をプレビュー用に装飾するHTMLを生成する
        public static string BuildPreviewHtml(string title, string content)
        {
            // 現在のアプリの実行スレッドから言語コードを取得する
            string currentLang = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            // --- HTMLデコード ---
            string decoded = WebUtility.HtmlDecode(content ?? "");

            // --- scriptタグ削除 ---
            decoded = ScriptTagRegex().Replace(decoded, "");

            // --- FC2フッター削除 ---
            decoded = Fc2FooterRegex().Replace(decoded, "");

            // --- 改行補正（タグが少ない場合のみ） ---
            if (!decoded.Contains("<p") && !decoded.Contains("<br"))
            {
                decoded = decoded.Replace("\r\n", "\n").Replace("\r", "\n");
                decoded = decoded.Replace("\n", "<br>");
            }

            // JSON内のキーに対応する翻訳を取得する
            string emptyMessage = T("PreviewHtml.Preview.None");

            // --- 空対策 --- 取得した翻訳メッセージをHTMLの要素として埋め込む
            string bodyContent = string.IsNullOrWhiteSpace(decoded)
                ? $"<p class='empty'>{emptyMessage}</p>"
                : decoded;

            // --- HTML構築 ---
            var sb = new StringBuilder();
            sb.Append($"<!DOCTYPE html><html lang='{currentLang}'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");

            sb.Append("<style>");
            sb.Append("body { font-family: -apple-system, system-ui, sans-serif; line-height: 1.8; padding: 24px; font-size: 16px;}");
            sb.Append("h2 { border-bottom: 2px solid; padding-bottom: 8px; font-size: 1.4em; }");
            sb.Append("img { max-width: 100%; height: auto; border-radius: 4px; }");
            sb.Append("pre { padding: 10px; overflow-x: auto; }");
            sb.Append("</style>");

            sb.Append("</head><body>");
            sb.Append($"<h2>{title}</h2>");
            sb.Append($"<div class='content'>{bodyContent}</div>");
            sb.Append("</body></html>");

            return sb.ToString();
        }
    }
}