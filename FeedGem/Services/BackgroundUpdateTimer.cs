using Microsoft.Win32;

namespace FeedGem.Services
{
    public class BackgroundUpdateTimer : IDisposable
    {
        private readonly FeedUpdateService _updateService;
        private readonly Func<Task> _onAfterUpdateAsync;
        private readonly Action _onTick;

        // UIスレッドで実行するためのDispatcher
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private TimeSpan _currentInterval;

        public BackgroundUpdateTimer(
            FeedUpdateService updateService,
            Func<Task> onAfterUpdateAsync,
            Action onTick,
            System.Windows.Threading.Dispatcher dispatcher)  // 追加
        {
            _updateService = updateService;
            _onAfterUpdateAsync = onAfterUpdateAsync;
            _onTick = onTick;
            _dispatcher = dispatcher; // 追加

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

        // タイマーのメインループ
        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                // 起動直後に1回実行（スリープ復帰対策）
                await ExecuteUpdateAsync();

                // 次のチックを待機
                while (await _timer!.WaitForNextTickAsync(token))
                {
                    await ExecuteUpdateAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // 正常終了（キャンセル）
            }
            catch (Exception ex)
            {
                // 予期しない例外（ログのみ）
                Console.WriteLine($"[Fatal] {ex.GetType().Name}: {ex.Message}");
            }
        }

        // フィード更新＋コールバック処理
        private async Task ExecuteUpdateAsync()
        {
            try
            {
                // フィード更新
                await _updateService.UpdateAllAsync();

                // 非同期コールバック
                if (_onAfterUpdateAsync != null)
                    await _onAfterUpdateAsync();

                // UI更新（Dispatcher経由で安全に実行）
                if (_onTick != null)
                {
                    await _dispatcher.InvokeAsync(_onTick);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// OSの電源状態が変わった時に呼ばれる。スリープ復帰時にタイマーを再起動する。
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                // タイマー再起動
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