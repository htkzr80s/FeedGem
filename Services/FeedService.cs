using FeedGem.Data;

namespace FeedGem.Services
{
    public class FeedService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // フィード取得＆記事保存
        public async Task FetchAndSaveEntriesAsync(long feedId, string url)
        {
            var http = HttpClientProvider.Client;

            try
            {
                // --- URL補正 ---
                string targetUrl = FeedUrlNormalizer.Normalize(url);

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
            }
            catch (Exception ex)
            {
                LoggingService.Error("記事取得失敗", ex);
            }
        }

        // 指定フィードをすべて既読にする
        public async Task MarkAllAsReadAsync(long feedId)
        {
            await _repository.MarkAllAsReadAsync(feedId);
        }

        // 指定したフォルダ内のすべてのフィード記事を既読にする
        public async Task MarkFolderAsReadAsync(string folderPath)
        {
            await _repository.MarkFolderAsReadAsync(folderPath);
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
            await _repository.RenameFolderAsync(folderPath, newName);
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