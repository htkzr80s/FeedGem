using System.Linq;
using System.Windows;
using FeedGem.Data;

public class TestArea
{
    public static async void Run()
    {
        var repo = new FeedRepository("feedgem.db");

        var feeds = await repo.GetAllFeedsAsync();
        var firstFeed = feeds.FirstOrDefault(f => !f.Url.StartsWith("folder://"));

        if (firstFeed == null)
        {
            MessageBox.Show("フィードなし");
            return;
        }

        var articles = await repo.GetEntriesByFeedIdAsync(firstFeed.Id);

        string result = string.Join("\n", articles.Select(a => $"{a.Date} | {a.Title}"));

        MessageBox.Show(result);
    }
}