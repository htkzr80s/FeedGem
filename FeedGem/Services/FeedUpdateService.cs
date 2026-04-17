using FeedGem.Data;
using static FeedGem.Data.FeedInfo;

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
                        // --- 404はスキップ ---
                        if (feed.ErrorState == FeedErrorState.NotFound404)
                            return;

                        await _feedService.FetchAndSaveEntriesAsync(feed.Id, feed.Url);

                        // --- 成功 ---
                        feed.ErrorState = FeedErrorState.None;
                        feed.LastSuccessTime = DateTime.Now;
                    }
                    catch (FeedNotFoundException)
                    {
                        // --- 404 ---
                        feed.ErrorState = FeedErrorState.NotFound404;
                        feed.LastFailureTime = DateTime.Now;

                        LoggingService.Error($"404: {feed.Title}", new Exception("404 Not Found"));
                    }
                    catch (Exception ex)
                    {
                        // --- 通信エラー ---
                        feed.LastFailureTime = DateTime.Now;

                        if (feed.LastSuccessTime != null)
                        {
                            var diff = DateTime.Now - feed.LastSuccessTime.Value;

                            if (diff.TotalHours >= 24)
                            {
                                feed.ErrorState = FeedErrorState.LongFailure;
                            }
                            else
                            {
                                feed.ErrorState = FeedErrorState.TemporaryFailure;
                            }
                        }
                        else
                        {
                            feed.ErrorState = FeedErrorState.TemporaryFailure;
                        }

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