using FeedGem.Data;
using FeedGem.Models;

namespace FeedGem.Services
{
    public class FeedUpdateService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;
        public event EventHandler? AllUpdatesCompleted;

        // 全フィード更新（UI・右クリック・定期処理すべてここに集約）
        public async Task UpdateAllAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();

            // 並列数制御
            var semaphore = new SemaphoreSlim(2);

            // リクエスト間隔のばらつき用（bot判定を避けるため固定値にしない）
            var rng = new Random();

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

                        // 各リクエストの前に 0.5〜2.0 秒のランダムウェイトを入れる
                        int delayMs = rng.Next(500, 2000);
                        await Task.Delay(delayMs);

                        // フィードの取得と記事の保存処理を実行
                        await _feedService.FetchEntriesAsync(feed.Id, feed.Url);

                        // 更新に成功したため、エラー状態をクリアし成功時刻を記録
                        feed.ErrorState = FeedInfo.FeedErrorState.None;
                        feed.LastSuccessTime = DateTime.Now;
                    }
                    catch (FeedNotFoundException)
                    {
                        // --- 404：恒久的なエラーとして記録 ---
                        feed.ErrorState = FeedInfo.FeedErrorState.NotFound404;
                        feed.LastFailureTime = DateTime.Now;
                        LoggingService.Error($"404: {feed.Title}", new Exception("404 Not Found"));
                    }
                    catch (FeedFormatException ex)  // ← 追加
                    {
                        // --- フィード形式エラー：通信は成功しているが内容が不正 ---
                        feed.ErrorState = FeedInfo.FeedErrorState.TemporaryFailure;
                        feed.LastFailureTime = DateTime.Now;
                        LoggingService.Error($"FormatError: {feed.Title}", ex);
                    }
                    catch (Exception ex)
                    {
                        // --- 通信エラー：成功実績の有無で深刻度を判定 ---
                        feed.LastFailureTime = DateTime.Now;

                        if (feed.LastSuccessTime != null)
                        {
                            var diff = DateTime.Now - feed.LastSuccessTime.Value;
                            feed.ErrorState = diff.TotalHours >= 24
                                ? FeedInfo.FeedErrorState.LongFailure
                                : FeedInfo.FeedErrorState.TemporaryFailure;
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
            _feedService.NotifyDataChanged();

            // 最後に1回だけ古い記事削除
            await _repository.DeleteOldEntriesAsync();

            OnAllUpdatesCompleted();
        }

        // 単体更新（将来UIから使える）
        public async Task UpdateSingleAsync(long feedId, string url)
        {
            await _feedService.FetchEntriesAsync(feedId, url);
            await _repository.DeleteOldEntriesAsync();
        }

        // イベント発行用のヘルパーメソッド
        protected virtual void OnAllUpdatesCompleted()
        {
            // UIスレッドで実行されるように配慮が必要な場合もあるが、まずはシンプルに発行
            AllUpdatesCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}