using Microsoft.Win32;

namespace FeedGem.Services
{
    public class BackgroundUpdateTimer : IDisposable
    {
        private readonly FeedUpdateService _updateService;
        // UI要素を再描画するためのコールバック（ツリーの読み込みなど）
        private readonly Action _onTick;

        // UIスレッドで実行するためのDispatcher
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        // 非同期タイマー本体
        private PeriodicTimer? _timer;
        // タイマー停止用のトークン
        private CancellationTokenSource? _cts;
        // 現在設定されている実行間隔
        private TimeSpan _currentInterval;

        public BackgroundUpdateTimer(
            FeedUpdateService updateService,
            Action onTick,
            System.Windows.Threading.Dispatcher dispatcher)
        {
            _updateService = updateService;
            _onTick = onTick;
            _dispatcher = dispatcher;

            // スリープからの復帰を監視するためのイベント登録
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        /// 指定された間隔でタイマーを開始する。既に動作している場合は再起動。
        public void Start(TimeSpan interval)
        {
            _currentInterval = interval;
            // 既存のタイマーを破棄して再作成
            StopCurrentTimer();

            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(interval);

            // 非同期ループを開始（完了を待たない）
            _ = RunAsync(_cts.Token);
        }

        // タイマーのメインループ
        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                // スリープ復帰時にネットワークが安定するまでの猶予として10秒待機
                await Task.Delay(10000, token);

                // 起動直後に1回実行
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
                Console.WriteLine($"[Fatal] {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 実際の更新処理を呼び出す
        private async Task ExecuteUpdateAsync()
        {
            try
            {
                await _updateService.UpdateAllAsync();

                // ツリーの再構築など、UI側の追加処理があれば実行
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