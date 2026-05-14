using FeedGem.Core;
using System.IO;

namespace FeedGem.Services
{
    public class ConfigManager
    {
        private static readonly string UserDataDirectory = Path.Combine(AppContext.BaseDirectory, AppConstants.UserDataFolderName);

        private static readonly string ConfigPath = Path.Combine(UserDataDirectory, AppConstants.ConfigFileName);

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
                // AppConfig のプロパティ一覧を取得
                var props = typeof(AppConfig).GetProperties();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('[')) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // キー名と一致するプロパティを探して値をセット
                    var prop = props.FirstOrDefault(p => p.Name == key);
                    if (prop == null) continue;

                    try
                    {
                        // プロパティの型に合わせて変換してセット
                        var converted = Convert.ChangeType(value, prop.PropertyType);
                        prop.SetValue(config, converted);
                    }
                    catch { /* 変換失敗時はデフォルト値のまま */ }
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

                // AppConfig のプロパティ一覧を自動で書き出す
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[FeedGem]");

                foreach (var prop in typeof(AppConfig).GetProperties())
                {
                    // プロパティ名=値 の形式で書き出す
                    sb.AppendLine($"{prop.Name}={prop.GetValue(config)}");
                }

                var content = sb.ToString();

                File.WriteAllText(ConfigPath, content);
            }
            catch { /* 保存失敗は無視（次回もデフォルトで） */ }
        }
    }

    // 設定をまとめたクラス
    public class AppConfig
    {
        // Auto / Dark / Light
        public string Theme { get; set; } = "Auto";
        // 言語設定（ロケールコードで保存。対応ファイルは埋め込みまたは Language フォルダから自動検索）
        public string Language { get; set; } = "en-US";
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public double FeedTreeWidth { get; set; } = 250;
        public double ArticleListWidth { get; set; } = 450;
        public int MaxArticleCount { get; set; } = 30;
        public static bool AllowInsecureHttp { get; set; } = false;
    }
}