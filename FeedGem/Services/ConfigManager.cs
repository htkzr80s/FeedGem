using System.IO;

namespace FeedGem.Services
{
    public class ConfigManager
    {
        // データベースと同じUserDataフォルダを定義
        private static readonly string UserDataDirectory = Path.Combine(AppContext.BaseDirectory, "UserData");

        // ConfigPathをUserData配下に変更
        private static readonly string ConfigPath = Path.Combine(UserDataDirectory, "FeedGem.ini");

        // 設定を読み込む（存在しなければデフォルト値を返す）
        public static AppConfig Load()
        {
            var config = new AppConfig();

            // フォルダが存在しない場合は作成
            if (!Directory.Exists(UserDataDirectory))
            {
                Directory.CreateDirectory(UserDataDirectory);
            }

            if (!File.Exists(ConfigPath))
            {
                Save(config);
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
                if (!Directory.Exists(UserDataDirectory))
                    Directory.CreateDirectory(UserDataDirectory);

                var content = $@"[FeedGem]
                    ; Theme (Auto / Dark / Light)
                    Theme={config.Theme}

                    ; ウィンドウ位置・サイズ
                    WindowLeft={config.WindowLeft}
                    WindowTop={config.WindowTop}
                    WindowWidth={config.WindowWidth}
                    WindowHeight={config.WindowHeight}

                    ; カラム幅
                    FeedTreeWidth={config.FeedTreeWidth}
                    ArticleListWidth={config.ArticleListWidth}
                    ";

                File.WriteAllText(ConfigPath, content);
            }
            catch { /* 保存失敗は無視（次回もデフォルトで） */ }
        }
    }

    // 設定をまとめたクラス
    public class AppConfig
    {
        public string Theme { get; set; } = "Auto";           // Auto / Dark / Light
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public double FeedTreeWidth { get; set; } = 250;
        public double ArticleListWidth { get; set; } = 450;
    }
}