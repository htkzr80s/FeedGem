using FeedGem.Data;
using System;
using System.Diagnostics;
using System.Linq;
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

        // 指定されたURLから記事を取得し、データベースに保存する
        public async Task FetchAndSaveEntriesAsync(string url)
        {
            // フィード情報取得
            var feeds = await _repository.GetAllFeedsAsync();
            var target = feeds.FirstOrDefault(f => f.Url == url);
            if (target == null) return;

            try
            {
                using var reader = XmlReader.Create(url);
                var feed = SyndicationFeed.Load(reader);

                if (feed == null) return;

                foreach (var item in feed.Items)
                {
                    string title = item.Title?.Text ?? "無題";
                    string link = item.Links.FirstOrDefault()?.Uri.ToString() ?? "";
                    string summary = item.Summary?.Text ?? "";

                    // summary補完
                    if (string.IsNullOrEmpty(summary) && item.Content is TextSyndicationContent textContent)
                    {
                        summary = textContent.Text;
                    }

                    summary ??= "";

                    // 日付安全取得
                    DateTimeOffset pubDate =
                        item.PublishDate != default ? item.PublishDate :
                        item.LastUpdatedTime != default ? item.LastUpdatedTime :
                        DateTimeOffset.Now;

                    await _repository.SaveEntryAsync(
                        target.Id,
                        title,
                        link,
                        summary,
                        pubDate.LocalDateTime.ToString("yyyy/MM/dd HH:mm")
                    );
                }

                // 古い記事削除
                await _repository.DeleteOldEntriesAsync();
            }
            catch (Exception ex)
            {
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