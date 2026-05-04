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

            // 2. 指定言語が英語以外なら、その内容で上書き（マージ）する
            if (cultureCode != "en-US")
            {
                JsonElement targetElement = LoadEmbeddedJson($"{ResourceBasePath}.{cultureCode}.json");

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

        public string GetText(string key)
        {
            return _translations.TryGetValue(key, out var text) ? text : key;
        }
    }
}