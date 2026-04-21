using FeedGem.Services;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FeedGem
{
    public partial class App : Application
    {
        // 2重起動防止用のミューテックスインスタンス
        private static Mutex? _mutex;

        // Shell32.dllの関数をインポート
        [LibraryImport("shell32.dll", SetLastError = true)]
        private static partial void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        // アプリケーション起動時の処理
        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "Yoshino.FeedGem.App.UniqueInstance", out bool createdNew);
            if (!createdNew)
            {
                Current.Shutdown();
                return;
            }

            // プロセス固有のAppIDを設定し、タスクバーでの意図しないグループ化を防止
            SetCurrentProcessExplicitAppUserModelID("Yoshino.FeedGem.App.v1");
            base.OnStartup(e);

            // 起動時に現在のテーマを適用
            var config = LoadConfig();
            ThemeManager.ApplyTheme(config.Theme);
        }

        // ConfigManagerへの呼び出しをApp.xaml.csで一元化
        // MainWindowや今後の設定メニューは必ずここを経由してConfigManagerに投げる
        public static AppConfig LoadConfig() => ConfigManager.Load();

        public static void SaveConfig(AppConfig config) => ConfigManager.Save(config);

        // WinForms非依存のためのWin32 API定義
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [LibraryImport("user32.dll")]
        private static partial IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        private const uint MONITOR_DEFAULTTONULL = 0x00000000;

        // マルチモニタ対策：ウィンドウが現在どの画面にも表示されない場合にプライマリモニタ中央へ移動
        public static void EnsureWindowOnScreen(Window window)
        {
            if (window == null) return;

            // ウィンドウの矩形を計算（Left/Top/Width/Heightから）
            RECT rect = new()
            {
                Left = (int)window.Left,
                Top = (int)window.Top,
                Right = (int)(window.Left + window.Width),
                Bottom = (int)(window.Top + window.Height)
            };

            // MonitorFromRectで矩形が任意のモニタと交差するか確認（交差なし＝完全に画面外）
            IntPtr hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL);

            if (hMonitor == IntPtr.Zero)
            {
                // プライマリモニタの作業領域中央に復帰
                var primary = System.Windows.SystemParameters.WorkArea;

                window.Left = primary.Left + (primary.Width - window.Width) / 2;
                window.Top = primary.Top + (primary.Height - window.Height) / 2;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        // 埋め込みリソースから高DPI向けのアイコンフレームを取得する
        public static ImageSource? GetHighDpiIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // ストリームからアイコンリソースを読み込み
            using var stream = assembly.GetManifestResourceStream("FeedGem.Resources.app.ico");
            if (stream == null) return null;

            // アイコンのデコーダーを作成し、全フレームを展開
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);

            // 最も解像度の高いフレームを取得し、高DPI環境でのぼやけを防止
            var highResFrame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();

            return highResFrame;
        }

        // テーマを画面に反映させるだけの処理（保存はしない）
        public static void ApplyThemePreview(string theme)
        {
            ThemeManager.ApplyTheme(theme);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // アプリ終了時にミューテックスを解放
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}