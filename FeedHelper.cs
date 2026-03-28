using System;
using System.Text;

namespace FeedGem.Helpers
{
    // フィード関連の複雑なロジックを管理するクラス
    public static class FeedHelper
    {
        // 記事の内容をプレビュー用に装飾するHTMLを生成する
        public static string GeneratePreviewHtml(string title, string content)
        {
            // 内容が空の場合のデフォルトメッセージ
            string bodyContent = string.IsNullOrWhiteSpace(content)
                ? "<p class='empty'>（プレビューを表示できる内容がありません。詳細はブラウザで確認してください。）</p>"
                : content;

            // モダンなデザインを適用したHTMLを構築
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='ja'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.Append("<style>");
            sb.Append("body { font-family: -apple-system, system-ui, sans-serif; line-height: 1.6; padding: 20px; color: #333; }");
            sb.Append("h2 { border-bottom: 2px solid #0078D7; padding-bottom: 8px; color: #000; font-size: 1.3em; }");
            sb.Append(".content { font-size: 14px; }");
            sb.Append(".empty { color: #666; font-style: italic; }");
            sb.Append("img { max-width: 100%; height: auto; border-radius: 4px; }"); // 画像の突き出し防止
            sb.Append("pre { background: #f4f4f4; padding: 10px; overflow-x: auto; }"); // コードブロック対応
            sb.Append("</style></head><body>");
            sb.Append($"<h2>{title}</h2>");
            sb.Append($"<div class='content'>{bodyContent}</div>");
            sb.Append("</body></html>");

            return sb.ToString();
        }

        // TODO: URLからフィードを探す複雑なロジックをここに移植する（現在はスタブ）
        // Step 4あたりでじっくり強化
        public static string FixFeedUrl(string inputUrl)
        {
            if (string.IsNullOrWhiteSpace(inputUrl)) return inputUrl;

            // 暫定的にトリム処理だけしておく
            return inputUrl.Trim();
        }
    }
}