namespace FeedGem.Core
{
    internal static class AppSettings
    {
        // フィード候補URLの最大表示件数
        internal static int MaxCandidateCount { get; set; } = 10;

        // データベースに保存する記事の最大件数
        internal static int MaxArticleCount { get; set; } = 30;
    }
}
