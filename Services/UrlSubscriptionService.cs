using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Views;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using MsgBox = System.Windows.MessageBox;

namespace FeedGem.Services
{
    public class UrlSubscriptionService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;

        // URLから購読処理を全部やる（UIも含めて完結）
        public async Task<bool> HandleCandidatesAsync(List<FeedCandidate> candidates, Window owner)
        {
            if (candidates.Count == 0)
            {
                MsgBox.Show("フィードが見つかりません。");
                return false;
            }

            if (candidates.Count == 1)
            {
                await AddFeedAsync(candidates[0]);
                return true;
            }

            var window = new FeedSelectionWindow(candidates)
            {
                Owner = owner
            };

            if (window.ShowDialog() == true)
            {
                foreach (var selected in window.SelectedFeeds)
                {
                    await AddFeedAsync(selected);
                }
                return true;
            }

            return false;
        }

        // フィード登録＋取得
        private async Task AddFeedAsync(FeedCandidate candidate)
        {
            long feedId = await _repository.AddFeedAsync("/", candidate.OriginalTitle, candidate.Url);

            // 先に記事取得
            await _feedService.FetchAndSaveEntriesAsync(feedId, candidate.Url);

            // ★ 記事が0なら削除
            var entries = await _repository.GetEntriesByFeedIdAsync(feedId);

            if (entries.Count == 0)
            {
                await _repository.DeleteFeedAsync(feedId);
            }
        }
    }
}