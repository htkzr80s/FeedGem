using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FeedGem.Services
{
    public class LocalizationService
    {
        public static LocalizationService Instance { get; } = new LocalizationService();

        // 最終的な翻訳結果を保持する辞書
        private Dictionary<string, string> _translations = [];

        // 埋め込みリソースのベースパス
        private const string ResourceBasePath = "FeedGem.Resources.Locales";

        // 言語が変更された時に通知するためのイベント
        public event Action? LanguageChanged;

        private LocalizationService()
        {
            // 初期化時にデフォルトの英語をロード
            LoadLanguage("en-US");
        }

        public void LoadLanguage(string cultureCode)
        {
            // 1. まず基礎となる英語をロード
            JsonElement englishElement = LoadEmbeddedJson($"{ResourceBasePath}.en-US.json");

            // 読み込みに成功していれば、階層を平坦化して辞書に格納
            if (englishElement.ValueKind != JsonValueKind.Undefined)
            {
                _translations = FlattenJson(englishElement);
            }

            // 2. 英語以外の場合、外部ファイル → 埋め込みリソースの順で上書き
            if (cultureCode != "en-US")
            {
                // 外部 Language フォルダを優先して試みる
                JsonElement targetElement = LoadExternalJson(cultureCode);

                // 外部になければ埋め込みリソースを試みる
                if (targetElement.ValueKind == JsonValueKind.Undefined)
                    targetElement = LoadEmbeddedJson($"{ResourceBasePath}.{cultureCode}.json");

                if (targetElement.ValueKind != JsonValueKind.Undefined)
                {
                    var flattenedTarget = FlattenJson(targetElement);
                    foreach (var item in flattenedTarget)
                    {
                        // 差分だけを上書きすることで、足りない項目は英語のまま残る
                        _translations[item.Key] = item.Value;
                    }
                }
            }

            // 言語の切り替えが終わった最後に、登録されているすべてのWindowへ通知を送る
            LanguageChanged?.Invoke();
        }

        // 戻り値を Dictionary ではなく JsonElement にしている点に注目
        private static JsonElement LoadEmbeddedJson(string resourcePath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourcePath);

            // リソースが見つからない場合は「未定義」の状態を返す
            if (stream == null) return default;

            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();

            // JSON全体を JsonDocument として解析し、その RootElement を返す
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone(); // CloneしないとDisposeで消えてしまうため
        }

        // 外部 Language フォルダから、locale が一致する JSON を検索して読み込む
        private static JsonElement LoadExternalJson(string cultureCode)
        {
            string langDir = Path.Combine(AppContext.BaseDirectory, "Language");
            if (!Directory.Exists(langDir)) return default;

            // フォルダ内の全JSONを走査してlocaleが一致するファイルを探す
            foreach (string file in Directory.GetFiles(langDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // _metadata.locale が一致するファイルを採用する
                    if (!root.TryGetProperty("_metadata", out var meta)) continue;
                    if (!meta.TryGetProperty("locale", out var localeProp)) continue;
                    if (localeProp.GetString() != cultureCode) continue;

                    // 最初に見つかったファイルを採用する（同一ロケールが複数ある場合は先勝ち）
                    System.Diagnostics.Debug.WriteLine($"[LocalizationService] Loaded external:"
                        + $"{Path.GetFileName(file)}");

                    return root.Clone();
                }
                catch
                {
                    // 壊れたファイルは無視してフォールバックに任せる
                }
            }
            return default;
        }

        // Language フォルダをスキャンして利用可能な言語一覧を返す
        public static List<LanguageEntry> DiscoverAvailableLanguages()
        {
            var result = new List<LanguageEntry>
            {
                // 組み込みの英語は常に先頭に追加
                new("English", "en-US"),
                new("日本語", "ja-JP")
            };

            string langDir = Path.Combine(AppContext.BaseDirectory, "Language");
            if (!Directory.Exists(langDir)) return result;

            // 登録済みロケールの重複チェック用
            var addedLocales = new HashSet<string> { "en-US", "ja-JP" };

            foreach (string file in Directory.GetFiles(langDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // _metadata セクションが存在しない場合はスキップ
                    if (!root.TryGetProperty("_metadata", out var meta)) continue;
                    if (!meta.TryGetProperty("language", out var langProp)) continue;
                    if (!meta.TryGetProperty("locale", out var localeProp)) continue;

                    string displayName = langProp.GetString() ?? "";
                    string locale = localeProp.GetString() ?? "";

                    // 組み込み済み言語リスト
                    var builtInLocales = new[] { "en-US", "ja-JP" };

                    // 組み込み済み言語はスキップ
                    if (builtInLocales.Contains(locale)) continue;

                    // 同一ロケールが複数ファイルに存在する場合は先勝ち
                    if (!addedLocales.Add(locale)) continue;

                    result.Add(new LanguageEntry(displayName, locale));
                }
                catch
                {
                    // 解析できないファイルは無視する
                }
            }

            return result;
        }

        // 言語エントリーの定義（表示名とカルチャコードのペア）
        public record LanguageEntry(string DisplayName, string CultureCode);

        // 階層構造を「Parent.Child」形式のフラットなキーに変換する
        private static Dictionary<string, string> FlattenJson(JsonElement element, string prefix = "")
        {
            var dict = new Dictionary<string, string>();

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    string key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    var childDict = FlattenJson(property.Value, key);
                    foreach (var kv in childDict) dict[kv.Key] = kv.Value;
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                dict[prefix] = element.GetString() ?? "";
            }

            return dict;
        }

        // インスタンス（Instance）経由で GetText を呼ぶだけの static メソッド
        public static string T(string key) => Instance.GetText(key);

        /// <param name="args">埋め込む値</param>
        public static string TF(string key, params object[] args)
        {
            // 指定したキーで翻訳を取得する
            string pattern = T(key);

            try
            {
                // 取得した文字列（例："URL copied: {0}"）に引数を流し込む
                return string.Format(pattern, args);
            }
            catch
            {
                // フォーマットに失敗した場合は、フォールバックとしてキーと値をそのまま出す
                return $"{key}: {string.Join(", ", args)}";
            }
        }

        public string GetText(string key)
        {
            return _translations.TryGetValue(key, out var text) ? text : key;
        }
    }
}