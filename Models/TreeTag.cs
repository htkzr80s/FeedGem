namespace FeedGem.Models
{
    public enum TreeNodeType
    {
        Folder,
        Feed
    }

    public class TreeTag
    {
        public TreeNodeType Type { get; init; }

        // Feed用
        public long? FeedId { get; init; }

        // Folder用
        public string? FolderPath { get; init; }

        // 表示名（共通）
        public string Name { get; set; } = string.Empty;

        // 未読数（キャッシュ）
        public int UnreadCount { get; set; }

        // favicon
        public string? Url { get; set; }
    }
}