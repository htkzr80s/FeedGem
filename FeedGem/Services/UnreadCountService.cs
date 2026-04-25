using FeedGem.Data;

namespace FeedGem.Services
{
    public class UnreadCountService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // 全フィードの未読件数を合計する
        public async Task<int> GetTotalUnreadAsync()
        {
            var map = await _repository.GetUnreadCountMapAsync();
            return map.Values.Sum();
        }
    }
}