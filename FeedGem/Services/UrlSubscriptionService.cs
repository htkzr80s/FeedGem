using FeedGem.Data;
using FeedGem.Models;
using System.ServiceModel.Syndication;
using System.Xml;

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
        Canceled,
        Error
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
            long feedId = -1;
            bool isNew = false;

            try
            {
                string title = candidate.OriginalTitle;

                // 空ならフィードから取得
                if (string.IsNullOrWhiteSpace(title))
                {
                    try
                    {
                        var http = HttpClientProvider.Client;
                        using var stream = await http.GetStreamAsync(candidate.Url);

                        using var reader = XmlReader.Create(stream);
                        var feed = SyndicationFeed.Load(reader);

                        title = feed?.Title?.Text ?? "";
                    }
                    catch
                    {
                        // タイトル取得失敗は無視
                    }
                }

                // それでも空ならURL
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = candidate.Url;
                }

                // --- 登録 ---
                (feedId, isNew) = await _repository.AddFeedAsync("/", title, candidate.Url);

                // --- 記事取得 ---
                await _feedService.FetchAndSaveEntriesAsync(feedId, candidate.Url);

                // --- 空チェック ---
                var entries = await _repository.GetEntriesByFeedIdAsync(feedId);
                if (entries.Count == 0)
                {
                    await _repository.DeleteFeedAsync(feedId);
                    return SubscribeResult.SkippedOrEmpty;
                }

                return SubscribeResult.Success;
            }
            catch (Exception ex)
            {
                LoggingService.Error("購読処理失敗", ex);

                // 無効なフィードが残っていたら削除
                if (isNew && feedId > 0)
                {
                    try
                    {
                        await _repository.DeleteFeedAsync(feedId);
                    }
                    catch (Exception deleteEx)
                    {
                        LoggingService.Error("フィード削除失敗", deleteEx);
                    }
                }

                return SubscribeResult.Error;
            }
        }
    }
}