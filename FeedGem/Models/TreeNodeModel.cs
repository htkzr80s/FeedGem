using FeedGem.Data;

namespace FeedGem.Models
{
    // ツリー表示用の純データモデル（UI非依存）
    public class TreeNodeModel
    {

        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public bool IsFolder { get; set; }

        public string Url { get; set; } = string.Empty;

        public int UnreadCount { get; set; } = 0;

        // エラー状態（FeedInfoの値をそのまま持つ）
        public FeedInfo.FeedErrorState ErrorState { get; set; } = FeedInfo.FeedErrorState.None;

        public List<TreeNodeModel> Children { get; set; } = [];
    }
}