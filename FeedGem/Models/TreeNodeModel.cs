using FeedGem.Data;

namespace FeedGem.Models
{
    // ツリー表示用の純データモデル（UI非依存）
    public class TreeNodeModel
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public long? FeedId { get; set; } // フィードなら値あり、フォルダならnull
        public string? Url { get; set; }
        public int UnreadCount { get; set; } = 0;

        // エラー状態（FeedInfoの値をそのまま持つ）
        public FeedInfo.FeedErrorState ErrorState { get; set; } = FeedInfo.FeedErrorState.None;

        public List<TreeNodeModel> Children { get; set; } = [];
    }
}