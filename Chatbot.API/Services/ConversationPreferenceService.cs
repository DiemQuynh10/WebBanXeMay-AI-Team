using System.Collections.Concurrent;
using System.Text;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ConversationPreferenceService : IConversationPreferenceService
    {
        private readonly ConcurrentDictionary<string, CustomerPreferenceProfile> _store = new();

        public Task<CustomerPreferenceProfile> GetAsync(string conversationId)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            return Task.FromResult(profile);
        }

        public Task<CustomerPreferenceProfile> MergeAsync(string conversationId, ParsedIntent intent)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            if (intent.PriceMin.HasValue)
                profile.PriceMin = intent.PriceMin;

            if (intent.PriceMax.HasValue)
                profile.PriceMax = intent.PriceMax;

            if (intent.TargetPrice.HasValue)
                profile.TargetPrice = intent.TargetPrice;

            if (intent.FilterType != PriceFilterType.None)
                profile.FilterType = intent.FilterType;

            if (!string.IsNullOrWhiteSpace(intent.Category))
                profile.PreferredCategory = intent.Category;

            if (!string.IsNullOrWhiteSpace(intent.Brand))
                profile.PreferredBrand = intent.Brand;

            if (!string.IsNullOrWhiteSpace(intent.Target))
                profile.Target = intent.Target;

            foreach (var item in intent.ExcludedCategories)
                profile.ExcludedCategories.Add(item);

            foreach (var item in intent.ExcludedBrands)
                profile.ExcludedBrands.Add(item);

            if (intent.HeightCm.HasValue)
                profile.HeightCm = intent.HeightCm;

            if (intent.NeedsLowSeat)
                profile.NeedsLowSeat = true;

            if (intent.ForSchool) profile.ForSchool = true;
            if (intent.ForWork) profile.ForWork = true;
            if (intent.ForCity) profile.ForCity = true;
            if (intent.ForTour) profile.ForTour = true;

            if (intent.WantsEasyControl) profile.WantsEasyControl = true;
            if (intent.WantsFuelSaving) profile.WantsFuelSaving = true;
            if (intent.WantsLargeStorage) profile.WantsLargeStorage = true;

            if (intent.PrefersMaleStyle) profile.PrefersMaleStyle = true;
            if (intent.PrefersFemaleStyle) profile.PrefersFemaleStyle = true;

            foreach (var style in intent.RequestedStyles)
                profile.RequestedStyles.Add(style);

            if (intent.MentionedProducts.Any())
            {
                profile.LastMentionedProducts = intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            if (!string.IsNullOrWhiteSpace(intent.ComparisonFeature))
            {
                profile.LastComparisonFeature = intent.ComparisonFeature;
            }
            if (!string.IsNullOrWhiteSpace(intent.IntentType))
            {
                profile.LastIntentType = intent.IntentType;
            }
            profile.TurnCount++;
            profile.LastUserMessage = intent.RawMessage;
            profile.UpdatedAtUtc = DateTime.UtcNow;

            return Task.FromResult(profile);
        }

        public Task SetRecommendedProductsAsync(
    string conversationId,
    IEnumerable<ProductSummaryDto> products,
    string answerMode = "fresh_consultation")
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            var items = products?
                .Where(x => x != null)
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList() ?? new List<ProductSummaryDto>();

            profile.LastRecommendedProductIds = items
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            profile.LastRecommendedProducts = items
                .Select(x => x.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.HasActiveRecommendationContext = items.Count > 0;
            profile.LastAnswerMode = answerMode;
            profile.UpdatedAtUtc = DateTime.UtcNow;

            return Task.CompletedTask;
        }
        public Task ClearRecommendationContextAsync(string conversationId)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.LastRecommendedProducts.Clear();
            profile.LastRecommendedProductIds.Clear();
            profile.HasActiveRecommendationContext = false;
            profile.LastAnswerMode = null;
            profile.LastComparisonFeature = null;
            profile.UpdatedAtUtc = DateTime.UtcNow;

            return Task.CompletedTask;
        }
        public Task ResetForFreshConsultationAsync(string conversationId)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.PriceMin = null;
            profile.PriceMax = null;
            profile.TargetPrice = null;
            profile.FilterType = PriceFilterType.None;

            profile.PreferredCategory = null;
            profile.PreferredBrand = null;
            profile.Target = null;

            profile.ExcludedCategories.Clear();
            profile.ExcludedBrands.Clear();

            profile.HeightCm = null;
            profile.NeedsLowSeat = false;

            profile.ForSchool = false;
            profile.ForWork = false;
            profile.ForCity = false;
            profile.ForTour = false;

            profile.WantsEasyControl = false;
            profile.WantsFuelSaving = false;
            profile.WantsLargeStorage = false;

            profile.PrefersMaleStyle = false;
            profile.PrefersFemaleStyle = false;

            profile.RequestedStyles.Clear();

            profile.LastRecommendedProducts.Clear();
            profile.LastRecommendedProductIds.Clear();
            profile.LastMentionedProducts.Clear();
            profile.LastComparedProducts.Clear();

            profile.HasActiveRecommendationContext = false;
            profile.LastAnswerMode = null;
            profile.LastComparisonFeature = null;
            profile.LastIntentType = null;
            profile.LastUserMessage = null;

            profile.UpdatedAtUtc = DateTime.UtcNow;

            return Task.CompletedTask;
        }

        public Task SetMentionedProductsAsync(string conversationId, IEnumerable<string> productNames)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.LastMentionedProducts = productNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.UpdatedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task SetComparedProductsAsync(string conversationId, IEnumerable<string> productNames)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.LastComparedProducts = productNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            profile.UpdatedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task SetLastIntentTypeAsync(string conversationId, string intentType)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.LastIntentType = intentType;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string conversationId)
        {
            _store.TryRemove(conversationId, out _);
            return Task.CompletedTask;
        }

        public string BuildProfileSummary(CustomerPreferenceProfile profile)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(profile.Target))
                parts.Add($"đối tượng: {profile.Target}");

            if (profile.TargetPrice.HasValue)
                parts.Add($"ngân sách khoảng {profile.TargetPrice.Value:N0} VNĐ");
            else
            {
                if (profile.PriceMin.HasValue && profile.PriceMax.HasValue)
                    parts.Add($"ngân sách từ {profile.PriceMin.Value:N0} đến {profile.PriceMax.Value:N0} VNĐ");
                else if (profile.PriceMax.HasValue)
                    parts.Add($"ngân sách tối đa {profile.PriceMax.Value:N0} VNĐ");
                else if (profile.PriceMin.HasValue)
                    parts.Add($"ngân sách từ {profile.PriceMin.Value:N0} VNĐ trở lên");
            }

            if (!string.IsNullOrWhiteSpace(profile.PreferredCategory))
                parts.Add($"ưu tiên {profile.PreferredCategory}");

            if (!string.IsNullOrWhiteSpace(profile.PreferredBrand))
                parts.Add($"ưu tiên hãng {profile.PreferredBrand}");

            if (profile.ExcludedCategories.Count > 0)
                parts.Add($"loại trừ: {string.Join(", ", profile.ExcludedCategories)}");

            if (profile.ExcludedBrands.Count > 0)
                parts.Add($"không muốn hãng: {string.Join(", ", profile.ExcludedBrands)}");

            if (profile.HeightCm.HasValue)
                parts.Add($"chiều cao khoảng {profile.HeightCm.Value}cm");

            if (profile.NeedsLowSeat)
                parts.Add("ưu tiên yên thấp, dễ chống chân");

            if (profile.ForSchool) parts.Add("nhu cầu đi học");
            if (profile.ForWork) parts.Add("nhu cầu đi làm");
            if (profile.ForCity) parts.Add("nhu cầu đi phố");
            if (profile.ForTour) parts.Add("nhu cầu đi đường dài");

            if (profile.WantsEasyControl) parts.Add("ưu tiên dễ điều khiển");
            if (profile.WantsFuelSaving) parts.Add("ưu tiên tiết kiệm xăng");
            if (profile.WantsLargeStorage) parts.Add("ưu tiên cốp rộng");

            if (profile.RequestedStyles.Count > 0)
                parts.Add($"phong cách: {string.Join(", ", profile.RequestedStyles)}");

            if (profile.LastRecommendedProducts.Count > 0)
                parts.Add($"các mẫu vừa gợi ý: {string.Join(", ", profile.LastRecommendedProducts)}");

            if (profile.LastMentionedProducts.Count > 0)
                parts.Add($"các mẫu đang nhắc tới: {string.Join(", ", profile.LastMentionedProducts)}");

            if (profile.LastComparedProducts.Count > 0)
                parts.Add($"cặp vừa so sánh: {string.Join(" vs ", profile.LastComparedProducts)}");

            if (!string.IsNullOrWhiteSpace(profile.LastIntentType))
                parts.Add($"intent gần nhất: {profile.LastIntentType}");
            if (profile.HasActiveRecommendationContext)
                parts.Add("đang có ngữ cảnh gợi ý trước đó");

            if (!string.IsNullOrWhiteSpace(profile.LastAnswerMode))
                parts.Add($"kiểu trả lời gần nhất: {profile.LastAnswerMode}");

            if (profile.LastRecommendedProductIds.Count > 0)
                parts.Add($"ids vừa gợi ý: {string.Join(", ", profile.LastRecommendedProductIds)}");

            if (profile.TurnCount > 0)
                parts.Add($"số lượt hội thoại: {profile.TurnCount}");
            if (parts.Count == 0)
                return "Chưa có hồ sơ nhu cầu rõ ràng.";

            var sb = new StringBuilder();
            sb.AppendLine("Hồ sơ nhu cầu hiện tại của người dùng:");
            foreach (var part in parts)
            {
                sb.AppendLine($"- {part}");
            }

            return sb.ToString().Trim();
        }
    }
}