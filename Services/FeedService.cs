using FeedGem.Data;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;

namespace FeedGem.Services
{
    public class FeedService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // すべてのフィードを巡回して最新記事を取得・保存する
        public async Task UpdateAllFeedsAsync()
        {
            // 登録されている全フィードを取得
            var feeds = await _repository.GetAllFeedsAsync();

            foreach (var feed in feeds)
            {
                try
                {
                    // インターネットからフィードをダウンロード
                    using var reader = XmlReader.Create(feed.Url);
                    var rssData = SyndicationFeed.Load(reader);

                    if (rssData == null) continue;

                    // 記事を一つずつチェックして保存
                    foreach (var item in rssData.Items)
                    {
                        // タイトルが空の場合の対策
                        string title = item.Title?.Text ?? "無題";

                        // URL取得
                        string url = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

                        // 概要取得
                        string summary = item.Summary?.Text ?? "";

                        // 日付安全取得
                        DateTimeOffset pubDate =
                            item.PublishDate != default ? item.PublishDate :
                            item.LastUpdatedTime != default ? item.LastUpdatedTime :
                            DateTimeOffset.Now;

                        // DB保存
                        await _repository.SaveEntryAsync(
                            feed.Id,
                            title,
                            url,
                            summary,
                            pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm")
                        );
                    }
                }
                catch (Exception ex)
                {
                    // 通信エラーなどはログに出すだけ
                    Debug.WriteLine($"フィード更新失敗: {feed.Title} - {ex.Message}");
                }
            }

            // 古い記事の削除
            await _repository.DeleteOldEntriesAsync();
        }

        // フィード取得＆記事保存
        public async Task FetchAndSaveEntriesAsync(long feedId, string url)
        {
            // 通信用のクライアント。UserAgentがないと拒否するサイトがあるため設定
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FeedGem/1.0");

            try
            {
                // FC2ブログ対策：必ず ?xml&all を付与する
                string targetUrl = url;

                if (url.Contains("blog.fc2.com"))
                {
                    if (!url.Contains("?xml"))
                    {
                        targetUrl = url.TrimEnd('/') + "/?xml";
                    }

                    if (!targetUrl.Contains("&all"))
                    {
                        targetUrl += "&all";
                    }
                }

                var stream = await http.GetStreamAsync(targetUrl);

                try
                {
                    // --- 通常の RSS 2.0 / Atom 形式の解析 ---
                    using var reader = XmlReader.Create(stream);
                    var feed = System.ServiceModel.Syndication.SyndicationFeed.Load(reader);

                    if (feed != null)
                    {
                        foreach (var item in feed.Items)
                        {
                            string title = item.Title?.Text ?? "";
                            string link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

                            // 本文の抽出ロジック（元コードのロジックを完全再現）
                            string summary = "";
                            var contentEncoded = item.ElementExtensions.FirstOrDefault(e => e.OuterName == "encoded" || e.OuterName == "content");
                            if (contentEncoded != null)
                            {
                                try
                                {
                                    using var extReader = contentEncoded.GetReader();
                                    var element = System.Xml.Linq.XElement.Load(extReader);
                                    summary = element.Value;
                                }
                                catch { }
                            }
                            if (string.IsNullOrEmpty(summary) && item.Content is System.ServiceModel.Syndication.TextSyndicationContent textContent)
                            {
                                summary = textContent.Text;
                            }
                            if (string.IsNullOrEmpty(summary))
                            {
                                summary = item.Summary?.Text ?? "";
                            }
                            if (string.IsNullOrEmpty(summary))
                            {
                                summary = $"<a href='{link}'>記事を開く</a>";
                            }

                            // 投稿日時の取得（日本時間に変換。ISO形式ではなく読みやすい形式へ）
                            DateTimeOffset pubDate = item.PublishDate != default ? item.PublishDate :
                                                   item.LastUpdatedTime != default ? item.LastUpdatedTime :
                                                   DateTimeOffset.Now;
                            string published = pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

                            await _repository.SaveEntryAsync(feedId, title, link, summary, published);
                        }
                        return;
                    }
                }
                catch
                {
                    // --- FC2ブログ等の RSS 1.0 (RDF) 形式の解析 ---
                    // ストリームを先頭に戻して再読み込み
                    var stream2 = await http.GetStreamAsync(targetUrl);
                    var doc = System.Xml.Linq.XDocument.Load(stream2);

                    // RSS 1.0 と Dublin Core (日付用) の名前空間を定義
                    System.Xml.Linq.XNamespace ns = "http://purl.org/rss/1.0/";
                    System.Xml.Linq.XNamespace dc = "http://purl.org/dc/elements/1.1/";

                    // <item> タグを抽出
                    var items = doc.Descendants(ns + "item");
                    foreach (var node in items)
                    {
                        string title = node.Element(ns + "title")?.Value ?? "";
                        string link = node.Element(ns + "link")?.Value ?? "";
                        string desc = node.Element(ns + "description")?.Value ?? "";

                        if (string.IsNullOrEmpty(desc))
                        {
                            desc = $"<a href='{link}'>記事を開く</a>";
                        }

                        // FC2の日付（dc:date）を解析。失敗した時だけ現在時刻にする
                        string dateVal = node.Element(dc + "date")?.Value ?? "";
                        string published;
                        if (DateTimeOffset.TryParse(dateVal, out var parsedDate))
                        {
                            published = parsedDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
                        }
                        else
                        {
                            published = DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                        }

                        await _repository.SaveEntryAsync(feedId, title, link, desc, published);
                    }
                }
            }
            catch (Exception ex)
            {
                // 通信エラーや解析エラーをデバッグ出力
                Debug.WriteLine($"記事取得失敗: {ex.Message}");
            }
        }

        // フィード名変更
        public async Task RenameFeedAsync(long feedId, string newName)
        {
            var feeds = await _repository.GetAllFeedsAsync();
            var target = feeds.FirstOrDefault(f => f.Id == feedId);
            if (target == null) return;

            await _repository.UpdateFeedAsync(target.Id, target.FolderPath, newName, target.Url);
        }

        // フォルダ名変更
        public async Task RenameFolderAsync(string folderPath, string newName)
        {
            var feeds = await _repository.GetAllFeedsAsync();

            string newPath = "/" + newName;

            // フォルダ内フィード更新
            foreach (var f in feeds.Where(f => f.FolderPath == folderPath))
            {
                await _repository.UpdateFeedAsync(f.Id, newPath, f.Title, f.Url);
            }

            // ダミーフォルダ更新
            string currentName = folderPath.TrimStart('/');
            var dummy = feeds.FirstOrDefault(f => f.Title == currentName && f.Url.StartsWith("folder://"));

            if (dummy != null)
            {
                await _repository.UpdateFeedAsync(dummy.Id, "/", newName, dummy.Url);
            }
        }

        // フィード削除
        public async Task DeleteFeedAsync(long feedId)
        {
            await _repository.DeleteFeedAsync(feedId);
        }

        // フォルダ削除
        public async Task DeleteFolderAsync(string folderPath)
        {
            await _repository.DeleteFolderAsync(folderPath);
        }
    }
}