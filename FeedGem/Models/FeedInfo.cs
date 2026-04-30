namespace FeedGem.Models
{
    // フィードやフォルダの情報を保持するデータモデル
    public class FeedInfo
    {
        public long Id { get; set; }
        public string FolderPath { get; set; } = "/";
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int SortOrder { get; set; } = 0;

        // エラー状態
        public FeedErrorState ErrorState { get; set; } = FeedErrorState.None;

        // 最終成功時刻
        public DateTime? LastSuccessTime { get; set; }

        // 最終失敗時刻
        public DateTime? LastFailureTime { get; set; }

        public enum FeedErrorState
        {
            None,
            NotFound404,
            TemporaryFailure,
            LongFailure
        }
    }
}