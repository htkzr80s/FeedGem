using System.Net;
using System.Net.Http;
using System.Text;

namespace FeedGem.Services
{
    public static class ArticleHtmlService
    {
        private static readonly HttpClient _httpClient = new();

        // 記事の内容をプレビュー用に装飾するHTMLを生成する
        public static string BuildPreviewHtml(string title, string content)
        {
            // --- HTMLデコード ---
            string decoded = System.Net.WebUtility.HtmlDecode(content ?? "");

            // --- scriptタグ削除 ---
            decoded = System.Text.RegularExpressions.Regex.Replace(
                decoded,
                "<script.*?</script>",
                "",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // --- FC2フッター削除 ---
            decoded = System.Text.RegularExpressions.Regex.Replace(
                decoded,
                "<div class=\"fc2_footer\".*?</div>",
                "",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // --- 改行補正（タグが少ない場合のみ） ---
            if (!decoded.Contains("<p") && !decoded.Contains("<br"))
            {
                decoded = decoded.Replace("\r\n", "\n").Replace("\r", "\n");
                decoded = decoded.Replace("\n", "<br>");
            }

            // --- 空対策 ---
            string bodyContent = string.IsNullOrWhiteSpace(decoded)
                ? "<p class='empty'>（プレビューを表示できる内容がありません。ブラウザで確認してください。）</p>"
                : decoded;

            // --- HTML構築 ---
            var sb = new System.Text.StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='ja'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");

            sb.Append("<style>");
            sb.Append("body { font-family: -apple-system, system-ui, sans-serif; line-height: 1.8; padding: 24px; color: #222; font-size: 16px; }");
            sb.Append("h2 { border-bottom: 2px solid #0078D7; padding-bottom: 8px; font-size: 1.4em; }");
            sb.Append(".content { font-size: 16px; }");
            sb.Append("p { margin-bottom: 1em; }");
            sb.Append("a { color: #0078D7; text-decoration: none; }");
            sb.Append("a:hover { text-decoration: underline; }");
            sb.Append("img { max-width: 100%; height: auto; border-radius: 4px; }");
            sb.Append("pre { background: #f4f4f4; padding: 10px; overflow-x: auto; }");
            sb.Append(".empty { color: #666; font-style: italic; }");
            sb.Append("</style>");

            sb.Append("</head><body>");
            sb.Append($"<h2>{title}</h2>");
            sb.Append($"<div class='content'>{bodyContent}</div>");
            sb.Append("</body></html>");

            return sb.ToString();
        }
    }
}