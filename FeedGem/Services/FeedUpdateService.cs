using FeedGem.Data;
using FeedGem.Models;

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

            // 各フィードに対する更新タスクを作成
            var tasks = feeds
                .Where(f => !string.IsNullOrEmpty(f.Url))
                .Select(async feed =>
                {
                    // セマフォが空くまで待機
                    await semaphore.WaitAsync();
                    try
                    {
                        // 404エラーが確定しているフィードは、無駄な通信を避けるためスキップ
                        if (feed.ErrorState == FeedInfo.FeedErrorState.NotFound404)
                        {
                            return;
                        }

                        // フィードの取得と記事の保存処理を実行
                        await _feedService.FetchEntriesAsync(feed.Id, feed.Url);

                        // 更新に成功したため、エラー状態をクリアし成功時刻を記録
                        feed.ErrorState = FeedInfo.FeedErrorState.None;
                        feed.LastSuccessTime = DateTime.Now;
                    }
                    catch (FeedNotFoundException)
                    {
                        // --- 404 ---
                        feed.ErrorState = FeedInfo.FeedErrorState.NotFound404;
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
                                feed.ErrorState = FeedInfo.FeedErrorState.LongFailure;
                            }
                            else
                            {
                                feed.ErrorState = FeedInfo.FeedErrorState.TemporaryFailure;
                            }
                        }
                        else
                        {
                            feed.ErrorState = FeedInfo.FeedErrorState.TemporaryFailure;
                        }

                        LoggingService.Error($"UpdateFailure: {feed.Title}", ex);
                    }
                    finally
                    {
                        await _repository.UpdateFeedStatusAsync(feed);
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
            await _feedService.FetchEntriesAsync(feedId, url);
            await _repository.DeleteOldEntriesAsync();
        }
    }
}