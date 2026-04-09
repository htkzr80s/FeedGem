using Microsoft.Win32;

namespace FeedGem.Services
{
    public class BackgroundUpdateTimer : IDisposable
    {
        private readonly FeedUpdateService _updateService;
        private readonly Func<Task> _onAfterUpdateAsync;
        private readonly Action _onTick;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private TimeSpan _currentInterval;

        public BackgroundUpdateTimer(
            FeedUpdateService updateService,
            Func<Task> onAfterUpdateAsync,
            Action onTick)
        {
            _updateService = updateService;
            _onAfterUpdateAsync = onAfterUpdateAsync;
            _onTick = onTick;

            // OSの電源状態変更イベントを購読（スリープ復帰対策）
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        /// 指定された間隔でタイマーを開始する。既に動作している場合は再起動。
        public void Start(TimeSpan interval)
        {
            _currentInterval = interval;
            StopCurrentTimer();

            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(interval);

            _ = RunAsync(_cts.Token);
        }

        /// タイマーのメインループ。WaitForNextTickAsyncで待機し、更新処理を回す。
        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                // 次のチック（間隔）が来るまで非同期で待機
                while (await _timer!.WaitForNextTickAsync(token))
                {
                    try
                    {
                        // フィードの更新処理を実行
                        await _updateService.UpdateAllAsync();

                        // 更新後のコールバック（トレイ通知など）を実行
                        if (_onAfterUpdateAsync != null)
                            await _onAfterUpdateAsync();

                        // 同期コールバック（UI上の時刻表示更新など）を実行
                        _onTick?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // 更新中のエラーをログ出力してループは継続
                        Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // CancellationTokenSourceのキャンセルによる正常な終了
            }
        }

        /// OSの電源状態が変わった時に呼ばれる。スリープ復帰時にタイマーを再起動する。
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                // 復帰時にタイマーを初期化し直して、即時または次サイクルから再開させる
                Start(_currentInterval);
            }
        }

        /// 現在のタイマーとキャンセル要求を停止・破棄する。
        private void StopCurrentTimer()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _timer?.Dispose();
        }

        /// リソースの解放。イベント購読の解除を忘れないようにする。
        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            StopCurrentTimer();
            GC.SuppressFinalize(this);
        }
    }
}