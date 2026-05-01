namespace FeedGem.Models
{
    public enum TreeNodeType
    {
        Folder,
        Feed
    }

    public class TreeTag
    {
        public long Id { get; init; }

        public TreeNodeType Type { get; init; }

        // 親のIDで管理
        public long? ParentId { get; set; }

        public string Name { get; set; } = string.Empty;

        public int UnreadCount { get; set; } = 0;

        // 並び順を保持
        public int SortOrder { get; set; }

        // favicon または フィードURL
        public string Url { get; set; } = string.Empty;
    }
}