namespace FeedGem.Core
{
    internal static class AppSettings
    {
        // フィード候補URLの最大表示件数
        internal static int MaxCandidateCount { get; set; } = 10;

        // データベースに保存する記事の最大件数
        internal static int MaxArticleCount { get; set; } = 30;

        // 非暗号化通信（http）を許可するかどうか
        internal static bool AllowInsecureHttp { get; set; } = false;

        // 現在の言語設定（デフォルトは英語：en-US）
        internal static string Language { get; set; } = "en-US";
    }
}
