using FeedGem.Data;
using FeedGem.Models;
using System;
using System.Diagnostics;
using System.IO;
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
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FeedGem/1.0");

            try
            {
                var stream = await http.GetStreamAsync(url);
                // --- まず通常（RSS2.0 / Atom）で読む ---
                try
                {
                    using var reader = XmlReader.Create(stream);
                    var feed = SyndicationFeed.Load(reader);

                    if (feed == null) return;

                    foreach (var item in feed.Items)
                    {
                        string title = item.Title?.Text ?? "";
                        string link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";

                        // --- 本文取得（元のロジックをそのまま維持） ---
                        string summary = "";
                        var contentEncoded = item.ElementExtensions
                            .FirstOrDefault(e =>
                                e.OuterName == "encoded" ||
                                e.OuterName == "content");
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

                        if (string.IsNullOrEmpty(summary) && item.Content is TextSyndicationContent textContent)
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

                        // --- 日付処理の修正（ここを改善しました） ---
                        DateTimeOffset pubDate = item.PublishDate != default ? item.PublishDate :
                                               item.LastUpdatedTime != default ? item.LastUpdatedTime :
                                               DateTimeOffset.Now;

                        // 日本の時刻形式「yyyy/MM/dd HH:mm」に変換して保存
                        string published = pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm");

                        await _repository.SaveEntryAsync(
                            feedId,
                            title,
                            link,
                            summary,
                            published
                        );
                    }
                    return;
                }
                catch
                {
                    // --- RDF（RSS1.0） fallback ---
                    var stream2 = await http.GetStreamAsync(url);
                    var doc = System.Xml.Linq.XDocument.Load(stream2);

                    var items = doc.Descendants()
                        .Where(x => x.Name.LocalName == "item");
                    foreach (var node in items)
                    {
                        string title = node.Elements().FirstOrDefault(x => x.Name.LocalName == "title")?.Value ?? "";
                        string link = node.Elements().FirstOrDefault(x => x.Name.LocalName == "link")?.Value ?? "";
                        string desc = node.Elements().FirstOrDefault(x => x.Name.LocalName == "description")?.Value ?? "";

                        if (string.IsNullOrEmpty(desc))
                        {
                            desc = $"<a href='{link}'>記事を開く</a>";
                        }

                        // RSS1.0でも日本時間で保存するように修正
                        string published = DateTime.Now.ToString("yyyy/MM/dd HH:mm");

                        await _repository.SaveEntryAsync(
                            feedId,
                            title,
                            link,
                            desc,
                            published
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"記事取得失敗: {ex.Message}");
            }
            return;
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