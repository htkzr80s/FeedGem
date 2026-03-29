using FeedGem.Models;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace FeedGem
{
    // フィード関連の複雑なロジックを管理するクラス
    public static class FeedHelper
    {
        private static readonly HttpClient _httpClient = new();

        // 指定されたURLからフィードの候補（RSS/Atom）を探し出す
        public static async Task<List<FeedCandidate>> DiscoverFeedsAsync(string targetUrl)
        {
            var candidates = new List<FeedCandidate>();

            // 1. まず入力されたURLそのものをフィードとして試す（SourceForge直入力などのケース）
            try
            {
                using var reader = XmlReader.Create(targetUrl);
                var feed = SyndicationFeed.Load(reader);
                candidates.Add(new FeedCandidate { Title = feed.Title.Text, Url = targetUrl });
                return candidates;
            }
            catch { /* 次のHTML解析へ */ }

            // 2. HTML内からフィードURLを探す
            try
            {
                var web = new HtmlWeb();
                var doc = await web.LoadFromWebAsync(targetUrl);

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
                        Uri baseUri = new(targetUrl);
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
                "/feeds/posts/default" // FC2やBlogger系
            };

            foreach (var path in commonPaths)
            {
                try
                {
                    Uri baseUri = new(targetUrl);
                    Uri testUri = new(baseUri, path);

                    using var reader = XmlReader.Create(testUri.AbsoluteUri);
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
    }
}