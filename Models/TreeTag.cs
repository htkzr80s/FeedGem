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
    }
}