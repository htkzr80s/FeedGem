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

        public string FolderPath { get; set; } = "/";

        public string Name { get; set; } = string.Empty;

        public int UnreadCount { get; set; } = 0;

        // favicon
        public string Url { get; set; } = string.Empty;
    }
}