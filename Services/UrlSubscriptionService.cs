using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FeedGem.Services
{
    // サービスのすぐ上で定義
    public enum SubscribeResult
    {
        Success,
        NoCandidates,
        TooManyCandidates,
        SkippedOrEmpty,
        Canceled
    }

    public class UrlSubscriptionService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;

        public async Task<SubscribeResult> HandleCandidatesAsync(List<FeedCandidate> candidates, Window owner)
        {
            // 1. 候補0件
            if (candidates.Count == 0) return SubscribeResult.NoCandidates;

            // 2. 候補が多すぎる（7件以上）
            if (candidates.Count >= 7) return SubscribeResult.TooManyCandidates;

            // 3. 候補が1件
            if (candidates.Count == 1)
            {
                return await AddFeedAsync(candidates[0]);
            }

            // 4. 候補が2～6件（選択ウィンドウを表示）
            var window = new FeedSelectionWindow(candidates) { Owner = owner };

            if (window.ShowDialog() == true)
            {
                // ラジオボタンで1つだけ選択する想定
                var selected = window.SelectedFeeds.FirstOrDefault();
                if (selected != null)
                {
                    return await AddFeedAsync(selected);
                }
            }

            return SubscribeResult.Canceled;
        }

        private async Task<SubscribeResult> AddFeedAsync(FeedCandidate candidate)
        {
            // ここで本来は重複チェック（URL重複など）を行う
            // もし重複なら return SubscribeResult.SkippedOrEmpty;

            long feedId = await _repository.AddFeedAsync("/", candidate.OriginalTitle, candidate.Url);

            // 記事取得
            await _feedService.FetchAndSaveEntriesAsync(feedId, candidate.Url);

            // 記事が0なら削除して「スキップ」扱いにする
            var entries = await _repository.GetEntriesByFeedIdAsync(feedId);
            if (entries.Count == 0)
            {
                await _repository.DeleteFeedAsync(feedId);
                return SubscribeResult.SkippedOrEmpty;
            }

            return SubscribeResult.Success;
        }
    }
}