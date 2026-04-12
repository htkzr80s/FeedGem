using FeedGem.Services;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FeedGem
{
    public partial class App : System.Windows.Application
    {
        // Shell32.dllの関数をインポート
        [LibraryImport("shell32.dll", SetLastError = true)]
        private static partial void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        // アプリケーション起動時の処理
        protected override void OnStartup(StartupEventArgs e)
        {
            // プロセス固有のAppIDを設定し、タスクバーでの意図しないグループ化を防止
            SetCurrentProcessExplicitAppUserModelID("Yoshino.FeedGem.App.v1");
            base.OnStartup(e);

            // 起動時に現在のテーマを適用
            var config = LoadConfig();
            ThemeManager.ApplyTheme(config.Theme);
        }

        public static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FeedGem",
            "Config.ini");

        public static readonly string ConfigDirectory = Path.GetDirectoryName(ConfigPath) ?? "";

        // ConfigManagerへの呼び出しをApp.xaml.csで一元化
        // MainWindowや今後の設定メニューは必ずここを経由してConfigManagerに投げる
        public static AppConfig LoadConfig() => ConfigManager.Load();

        public static void SaveConfig(AppConfig config) => ConfigManager.Save(config);

        // マルチモニタ対策：ウィンドウが現在どの画面にも表示されない場合にプライマリモニタ中央へ移動
        public static void EnsureWindowOnScreen(Window window)
        {
            if (window == null) return;

            // 現在の全スクリーンの作業領域をチェック
            bool isOnAnyScreen = false;
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                // ウィンドウの左上と右下が少なくとも一部スクリーン内にあるか簡易判定
                if (window.Left + window.Width * 0.1 >= screen.WorkingArea.Left &&
                    window.Left + window.Width * 0.9 <= screen.WorkingArea.Right &&
                    window.Top + window.Height * 0.1 >= screen.WorkingArea.Top &&
                    window.Top + window.Height * 0.9 <= screen.WorkingArea.Bottom)
                {
                    isOnAnyScreen = true;
                    break;
                }
            }

            if (!isOnAnyScreen)
                {
                    // プライマリモニタの中央に復帰（nullチェックを追加）
                    var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                    if (primaryScreen != null)
                    {
                        var primary = primaryScreen.WorkingArea;

                        window.Left = primary.Left + (primary.Width - window.Width) / 2;
                        window.Top = primary.Top + (primary.Height - window.Height) / 2;
                        window.WindowStartupLocation = WindowStartupLocation.Manual;
                    }
                    // PrimaryScreenがnullの場合（極めて稀）は何もしない（安全側）
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

        // テーマ切り替え（ConfigManagerとThemeManagerを一元管理）
        public static void ChangeTheme(string theme)
        {
            var config = LoadConfig();
            config.Theme = theme;
            SaveConfig(config);
            ThemeManager.ApplyTheme(theme);
        }
    }
}