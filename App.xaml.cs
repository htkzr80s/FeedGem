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
    }
}