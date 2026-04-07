using System;
using System.Threading;
using System.Threading.Tasks;

namespace FeedGem.Services
{
    public class BackgroundUpdateTimer(
        FeedUpdateService updateService,
        Func<Task> onAfterUpdateAsync,
        Action onTick) : IDisposable
    {
        private readonly FeedUpdateService _updateService = updateService;
        private readonly Func<Task> _onAfterUpdateAsync = onAfterUpdateAsync;
        private readonly Action _onTick = onTick;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;

        // タイマー開始
        public void Start(TimeSpan interval)
        {
            Dispose(); // 既存停止
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(interval);

            _ = RunAsync(_cts.Token);
        }

        // メインループ
        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(token))
                {
                    try
                    {
                        // フィード更新
                        await _updateService.UpdateAllAsync();

                        // トレイ更新など（非同期）
                        if (_onAfterUpdateAsync != null)
                            await _onAfterUpdateAsync();

                        // UIの時刻更新など（同期）
                        _onTick?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常終了
            }
        }

        // 停止処理
        public void Dispose()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}