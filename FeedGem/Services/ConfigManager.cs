using System.IO;

namespace FeedGem.Services
{
    public class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,  // exeがあるディレクトリ
            "FeedGem.ini");  // ファイル名をFeedGem.iniに（.ini拡張子で明確に）

        private static readonly string ConfigDirectory = Path.GetDirectoryName(ConfigPath) ?? "";

        // 設定を読み込む（存在しなければデフォルト値を返す）
        public static AppConfig Load()
        {
            var config = new AppConfig();

            if (!File.Exists(ConfigPath))
            {
                Save(config); // ConfigManager自身の責務で保存
                return config;
            }

            try
            {
                var lines = File.ReadAllLines(ConfigPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';')) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "Theme":
                            config.Theme = value;
                            break;
                        case "WindowLeft":
                            if (double.TryParse(value, out double left)) config.WindowLeft = left;
                            break;
                        case "WindowTop":
                            if (double.TryParse(value, out double top)) config.WindowTop = top;
                            break;
                        case "WindowWidth":
                            if (double.TryParse(value, out double width)) config.WindowWidth = width;
                            break;
                        case "WindowHeight":
                            if (double.TryParse(value, out double height)) config.WindowHeight = height;
                            break;
                        case "FeedTreeWidth":
                            if (double.TryParse(value, out double treeW)) config.FeedTreeWidth = treeW;
                            break;
                        case "ArticleListWidth":
                            if (double.TryParse(value, out double listW)) config.ArticleListWidth = listW;
                            break;
                        case "FirstLaunch":
                            if (bool.TryParse(value, out bool first)) config.FirstLaunch = first;
                            break;
                    }
                }
            }
            catch { /* 読み込み失敗時はデフォルト */ }

            return config;
        }
        // 設定を保存
        public static void Save(AppConfig config)
        {
            try
            {
                if (!Directory.Exists(ConfigDirectory))
                    Directory.CreateDirectory(ConfigDirectory);

                var content = $@"[FeedGem]
                    ; テーマ設定 (Auto / Dark / Light)
                    Theme={config.Theme}

                    ; ウィンドウ位置・サイズ
                    WindowLeft={config.WindowLeft}
                    WindowTop={config.WindowTop}
                    WindowWidth={config.WindowWidth}
                    WindowHeight={config.WindowHeight}

                    ; カラム幅
                    FeedTreeWidth={config.FeedTreeWidth}
                    ArticleListWidth={config.ArticleListWidth}
                    FirstLaunch={config.FirstLaunch}
                    ";

                File.WriteAllText(ConfigPath, content);
            }
            catch { /* 保存失敗は無視（次回もデフォルトで） */ }
        }
    }

    // 設定をまとめたクラス
    public class AppConfig
    {
        public string Theme { get; set; } = "Auto";           // Auto / Dark / Light / User
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public double FeedTreeWidth { get; set; } = 250;
        public double ArticleListWidth { get; set; } = 450;
        public bool FirstLaunch { get; set; } = true;         // 初回起動フラグ
    }
}