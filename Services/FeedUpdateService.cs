using FeedGem.Data;

namespace FeedGem.Services
{
    public class FeedUpdateService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;

        // 全フィード更新（UI・右クリック・定期処理すべてここに集約）
        public async Task UpdateAllAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();

            // 並列数制御
            var semaphore = new SemaphoreSlim(5);

            var tasks = feeds
                .Where(f => !f.Url.StartsWith("folder://"))
                .Select(async feed =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await _feedService.FetchAndSaveEntriesAsync(feed.Id, feed.Url);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Error($"更新失敗: {feed.Title}", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(tasks);

            // 最後に1回だけ古い記事削除
            await _repository.DeleteOldEntriesAsync();
        }

        // 単体更新（将来UIから使える）
        public async Task UpdateSingleAsync(long feedId, string url)
        {
            await _feedService.FetchAndSaveEntriesAsync(feedId, url);
            await _repository.DeleteOldEntriesAsync();
        }
    }
}