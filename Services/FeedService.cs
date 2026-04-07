using FeedGem.Data;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using static System.Net.WebRequestMethods;

namespace FeedGem.Services
{
    public class FeedService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // すべてのフィードを巡回して最新記事を取得・保存する
        public async Task UpdateAllFeedsAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();
            var http = HttpClientProvider.Client;
            var semaphore = new SemaphoreSlim(5);

            var tasks = feeds.Select(async feed =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var stream = await http.GetStreamAsync(feed.Url);
                    var articles = FeedParser.Parse(stream);

                    foreach (var article in articles)
                    {
                        await _repository.SaveEntryAsync(
                            feed.Id,
                            article.Title,
                            article.Url,
                            article.Summary,
                            article.Date
                        );
                    }
                }
                catch (Exception ex)
                {
                    // エラー時ログ
                    LoggingService.Error($"フィード更新失敗: {feed.Title} ({feed.Url})", ex);
                }
            });

            await Task.WhenAll(tasks);

            // 古い記事の削除
            await _repository.DeleteOldEntriesAsync();
        }

        // フィード取得＆記事保存
        public async Task FetchAndSaveEntriesAsync(long feedId, string url)
        {
            var http = HttpClientProvider.Client;

            try
            {
                // --- URL補正 ---
                string targetUrl = SiteUrlHelper.Normalize(url);

                // FC2対策
                if (url.Contains("blog.fc2.com"))
                {
                    if (!targetUrl.Contains("?xml"))
                    {
                        targetUrl = targetUrl.TrimEnd('/') + "/?xml";
                    }

                    if (!targetUrl.Contains("&all"))
                    {
                        targetUrl += "&all";
                    }
                }

                using var stream = await http.GetStreamAsync(targetUrl);
                var articles = FeedParser.Parse(stream)
                    .OrderByDescending(a => a.Date)
                    .Take(30)
                    .ToList();

                foreach (var article in articles)
                {
                    await _repository.SaveEntryAsync(
                        feedId,
                        article.Title,
                        article.Url,
                        article.Summary,
                        article.Date
                    );
                }
                await _repository.DeleteOldEntriesAsync();
            }
            catch (Exception ex)
            {
                LoggingService.Error("記事取得失敗", ex);
            }
        }

        // 指定フィードをすべて既読にする
        public async Task MarkAllAsReadAsync(long feedId)
        {
            var entries = await _repository.GetEntriesByFeedIdAsync(feedId);

            foreach (var entry in entries)
            {
                await _repository.MarkAsReadAsync(entry.Url);
            }
        }

        // 指定したフォルダ内のすべてのフィード記事を既読にする
        public async Task MarkFolderAsReadAsync(string folderPath)
        {
            var feeds = await _repository.GetAllFeedsAsync();

            // 対象フォルダ内のフィードを抽出（前方一致でサブフォルダも含む）
            var targetFeeds = feeds.Where(f => f.FolderPath == folderPath || f.FolderPath.StartsWith(folderPath + "/"));

            foreach (var feed in targetFeeds)
            {
                if (feed.Url.StartsWith("folder://")) continue;

                var entries = await _repository.GetEntriesByFeedIdAsync(feed.Id);
                foreach (var entry in entries)
                {
                    await _repository.MarkAsReadAsync(entry.Url);
                }
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