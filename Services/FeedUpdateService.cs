using FeedGem.Data;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FeedGem.Services
{
    public class FeedUpdateService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;

        // 全フィード更新（UI・右クリック・定期処理すべてここに集約）
        public async Task UpdateAllAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();

            foreach (var feed in feeds.Where(f => !f.Url.StartsWith("folder://")))
            {
                try
                {
                    await _feedService.FetchAndSaveEntriesAsync(feed.Id, feed.Url);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"更新失敗: {feed.Title} - {ex.Message}");
                }
            }

            // 古い記事削除もここに統一
            await _repository.DeleteOldEntriesAsync();
        }

        // 単体更新（将来UIから使える）
        public async Task UpdateSingleAsync(long feedId, string url)
        {
            await _feedService.FetchAndSaveEntriesAsync(feedId, url);
            await _repository.DeleteOldEntriesAsync();
        }
    }
}