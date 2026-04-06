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
        NeedsSelection,
        NoCandidates,
        TooManyCandidates,
        SkippedOrEmpty,
        Canceled
    }

    public class UrlSubscriptionService(FeedRepository repository, FeedService feedService)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;

        public async Task<SubscribeResult> HandleCandidatesAsync(List<FeedCandidate> candidates)
        {
            if (candidates.Count == 0) return SubscribeResult.NoCandidates;

            if (candidates.Count >= 7) return SubscribeResult.TooManyCandidates;

            if (candidates.Count == 1) return await AddFeedAsync(candidates[0]);

            if (candidates.Count > 1) return SubscribeResult.NeedsSelection;

            return SubscribeResult.Canceled;
        }

        public async Task<SubscribeResult> AddFeedAsync(FeedCandidate candidate)
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