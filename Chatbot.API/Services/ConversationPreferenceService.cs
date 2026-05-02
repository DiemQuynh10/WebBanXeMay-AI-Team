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
            profile.LastMentionedProducts ??= new List<string>();
            profile.LastRecommendedProducts ??= new List<string>();
            profile.LastComparedProducts ??= new List<string>();
            profile.BaseRecommendedProducts ??= new List<string>();
            profile.LastSearchProductNames ??= new List<string>();
            profile.LastRecommendedProductIds ??= new List<int>();
            profile.LastSearchProductIds ??= new List<int>();

            profile.LastLookupCandidateNames ??= new List<string>();
            profile.ExcludedProducts ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            profile.ExcludedBrands ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            profile.ExcludedCategories ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            profile.RequestedStyles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(profile);
        }

        public Task<CustomerPreferenceProfile> MergeAsync(
     string conversationId,
     ParsedIntent intent,
     bool isFreshRecommendation = false)
        {
            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            var genderSignals = DetectGenderSignals(intent.RawMessage);
            if (intent.IsDirectProductLookup ||
    string.Equals(intent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase))
            {
                ApplyMetadataUpdates(profile, intent);
                FinalizeProfileUpdate(profile, intent);
                return Task.FromResult(profile);
            }

            if (isFreshRecommendation)
            {
                ResetUserPreferenceState(profile);
                ResetCompareContextForFreshRecommendation(profile);
                ApplyFreshRecommendationProfile(profile, intent, genderSignals.MentionsMale, genderSignals.MentionsFemale);
            }
            else
            {
                ApplyMergedRecommendationProfile(profile, intent, genderSignals.MentionsMale, genderSignals.MentionsFemale);
            }

            ApplyMetadataUpdates(profile, intent);
            FinalizeProfileUpdate(profile, intent);

            return Task.FromResult(profile);
        }
        private readonly record struct GenderSignals(bool MentionsMale, bool MentionsFemale);
        private static GenderSignals DetectGenderSignals(string? rawMessage)
        {
            var raw = NormalizeGenderText(rawMessage);

            bool mentionsMale =
                raw.Contains(" nam ") ||
                raw.StartsWith("nam ") ||
                raw.EndsWith(" nam") ||
                raw.Contains("cho nam") ||
                raw.Contains("phai nam");

            bool mentionsFemale =
                raw.Contains(" nu ") ||
                raw.StartsWith("nu ") ||
                raw.EndsWith(" nu") ||
                raw.Contains("cho nu") ||
                raw.Contains("phai nu") ||
                raw.Contains("phu nu");

            return new GenderSignals(mentionsMale, mentionsFemale);
        }
        private static void ResetUserPreferenceState(CustomerPreferenceProfile profile)
        {
            profile.PriceMin = null;
            profile.PriceMax = null;
            profile.TargetPrice = null;
            profile.FilterType = PriceFilterType.None;

            profile.PreferredCategory = null;
            profile.PreferredBrand = null;
            profile.Target = null;

            profile.ExcludedCategories.Clear();
            profile.ExcludedBrands.Clear();
            profile.ExcludedProducts.Clear();

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
        }
        private static void ResetCompareContextForFreshRecommendation(CustomerPreferenceProfile profile)
        {
            profile.HasActiveCompareContext = false;
            profile.LastComparisonFeature = null;
        }
        private static void ApplyMergedRecommendationProfile(
    CustomerPreferenceProfile profile,
    ParsedIntent intent,
    bool messageExplicitlyMentionsMale,
    bool messageExplicitlyMentionsFemale)
        {
            ApplyPriceState(profile, intent);

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                profile.PreferredCategory = intent.Category;
                profile.ExcludedCategories.RemoveWhere(x =>
                    string.Equals(x, intent.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                profile.PreferredBrand = intent.Brand;
                profile.ExcludedBrands.RemoveWhere(x =>
                    string.Equals(x, intent.Brand, StringComparison.OrdinalIgnoreCase));
            }

            ApplyTargetState(profile, intent, messageExplicitlyMentionsMale, messageExplicitlyMentionsFemale);
            ApplyPreferenceFlags(profile, intent);
            if (!string.IsNullOrWhiteSpace(profile.PreferredBrand) &&
    profile.ExcludedBrands.Contains(profile.PreferredBrand))
            {
                profile.PreferredBrand = null;
            }

            if (!string.IsNullOrWhiteSpace(profile.PreferredCategory) &&
                profile.ExcludedCategories.Contains(profile.PreferredCategory))
            {
                profile.PreferredCategory = null;
            }
        }
        private static void ApplyMetadataUpdates(CustomerPreferenceProfile profile, ParsedIntent intent)
        {
            if (intent.MentionedProducts.Any())
            {
                profile.LastMentionedProducts = intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(intent.ComparisonFeature))
                profile.LastComparisonFeature = intent.ComparisonFeature;

            if (!string.IsNullOrWhiteSpace(intent.IntentType))
                profile.LastIntentType = intent.IntentType;

            if (!string.IsNullOrWhiteSpace(intent.RouteFlow) &&
                !string.Equals(intent.RouteFlow, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                profile.ActiveFlow = intent.RouteFlow;
            }

            if (intent.IsDirectCompare)
                profile.HasActiveCompareContext = true;
        }
        private static void FinalizeProfileUpdate(CustomerPreferenceProfile profile, ParsedIntent intent)
        {
            profile.TurnCount++;
            profile.LastUserMessage = intent.RawMessage;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }
        private static void ApplyPriceState(CustomerPreferenceProfile profile, ParsedIntent intent)
        {
            if (intent.FilterType != PriceFilterType.None)
            {
                profile.FilterType = intent.FilterType;

                switch (intent.FilterType)
                {
                    case PriceFilterType.Range:
                        profile.PriceMin = intent.PriceMin;
                        profile.PriceMax = intent.PriceMax;
                        profile.TargetPrice = null;
                        break;

                    case PriceFilterType.MaxOnly:
                        profile.PriceMin = null;
                        profile.PriceMax = intent.PriceMax;
                        profile.TargetPrice = null;
                        break;

                    case PriceFilterType.MinOnly:
                        profile.PriceMin = intent.PriceMin;
                        profile.PriceMax = null;
                        profile.TargetPrice = null;
                        break;

                    case PriceFilterType.Around:
                        profile.TargetPrice = intent.TargetPrice;
                        profile.PriceMin = intent.PriceMin;
                        profile.PriceMax = intent.PriceMax;
                        break;
                }
            }
            else
            {
                if (intent.PriceMin.HasValue)
                    profile.PriceMin = intent.PriceMin;

                if (intent.PriceMax.HasValue)
                    profile.PriceMax = intent.PriceMax;

                if (intent.TargetPrice.HasValue)
                    profile.TargetPrice = intent.TargetPrice;
            }
        }
        public async Task SaveProductLookupContextAsync(
    string conversationId,
    int? productId,
    string? productName,
    IEnumerable<string>? candidateNames = null)
        {
            var profile = await GetAsync(conversationId);

            profile.LastLookupProductId = productId;
            profile.LastLookupProductName = productName;

            profile.LastResolvedProductId = productId;
            profile.LastResolvedProductName = productName;

            profile.LastLookupCandidateNames = candidateNames?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
            if (candidateNames != null)
            {
                var mentioned = candidateNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mentioned.Count > 0)
                {
                    profile.LastMentionedProducts = mentioned;
                }
                else if (!string.IsNullOrWhiteSpace(productName))
                {
                    profile.LastMentionedProducts = new List<string> { productName };
                }
            }
            else if (!string.IsNullOrWhiteSpace(productName))
            {
                profile.LastMentionedProducts = new List<string> { productName };
            }

            profile.ActiveFlow = ChatFlowType.ProductLookup;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }

        public async Task SaveResolvedOrderContextAsync(
            string conversationId,
            int? orderId,
            string? phone)
        {
            var profile = await GetAsync(conversationId);

            profile.LastResolvedOrderId = orderId;
            profile.LastResolvedOrderPhone = phone;

            profile.ActiveFlow = ChatFlowType.OrderLookup;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }
        public async Task ClearOrderLookupPendingAsync(string conversationId)
        {
            var profile = await GetAsync(conversationId);

            profile.HasPendingOrderLookup = false;
            profile.PendingOrderId = null;
            profile.PendingOrderPhone = null;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }

        public async Task ClearProductLookupContextAsync(string conversationId)
        {
            var profile = await GetAsync(conversationId);

            profile.LastLookupProductId = null;
            profile.LastLookupProductName = null;
            profile.LastResolvedProductId = null;
            profile.LastResolvedProductName = null;
            profile.LastLookupCandidateNames = new List<string>();
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }
        private static void ApplyTargetState(
    CustomerPreferenceProfile profile,
    ParsedIntent intent,
    bool messageExplicitlyMentionsMale,
    bool messageExplicitlyMentionsFemale)
        {
            if (!string.IsNullOrWhiteSpace(intent.Target))
            {
                var normalizedTarget = NormalizeGenderText(intent.Target);

                if (messageExplicitlyMentionsMale && normalizedTarget.Contains("nam"))
                {
                    profile.Target = "nam";
                    profile.PrefersMaleStyle = true;
                    profile.PrefersFemaleStyle = false;
                }
                else if (messageExplicitlyMentionsFemale && normalizedTarget.Contains("nu"))
                {
                    profile.Target = "nữ";
                    profile.PrefersFemaleStyle = true;
                    profile.PrefersMaleStyle = false;
                }
                else
                {
                    profile.Target = intent.Target;
                }
            }

            if (messageExplicitlyMentionsMale)
            {
                profile.Target = "nam";
                profile.PrefersMaleStyle = true;
                profile.PrefersFemaleStyle = false;
            }
            else if (messageExplicitlyMentionsFemale)
            {
                profile.Target = "nữ";
                profile.PrefersFemaleStyle = true;
                profile.PrefersMaleStyle = false;
            }
        }
        private static void ApplyPreferenceFlags(CustomerPreferenceProfile profile, ParsedIntent intent)
        {
            foreach (var item in intent.ExcludedCategories)
                profile.ExcludedCategories.Add(item);

            foreach (var item in intent.ExcludedBrands)
                profile.ExcludedBrands.Add(item);
            foreach (var item in intent.ExcludedProducts)
                profile.ExcludedProducts.Add(item);
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

            if (intent.PrefersMaleStyle && !intent.PrefersFemaleStyle)
            {
                profile.PrefersMaleStyle = true;
                profile.PrefersFemaleStyle = false;
            }
            else if (intent.PrefersFemaleStyle && !intent.PrefersMaleStyle)
            {
                profile.PrefersFemaleStyle = true;
                profile.PrefersMaleStyle = false;
            }

            foreach (var style in intent.RequestedStyles)
                profile.RequestedStyles.Add(style);
        }
        public Task UpdateCurrentRecommendedProductsAsync(
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
            profile.CurrentRecommendedProducts = items
    .Select(x => x.Ten)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
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
            profile.ActiveFlow = answerMode switch
            {
                "followup" => ChatFlowType.RecommendationFollowUp,
                "refine" => ChatFlowType.Refinement,
                _ => ChatFlowType.Recommendation
            };
            if (items.Count > 0 &&
    (string.Equals(answerMode, "fresh_consultation", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(answerMode, "followup", StringComparison.OrdinalIgnoreCase)))
            {
                profile.BaseRecommendedProductIds = items
                    .Select(x => x.Id)
                    .Distinct()
                    .ToList();

                profile.BaseRecommendedProducts = items
                    .Select(x => x.Ten)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            // khi recommendation mới được tạo, không nên giữ lookup flow cũ là flow hiện hành
            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName) &&
                !string.Equals(profile.ActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                // giữ dữ liệu lookup để tham khảo, nhưng recommendation là flow chính hiện tại
            }
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

            if (string.Equals(profile.ActiveFlow, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.ActiveFlow, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.ActiveFlow, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.ActiveFlow, ChatFlowType.BrandSwitch, StringComparison.OrdinalIgnoreCase))
            {
                profile.ActiveFlow = null;
            }

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
            profile.ExcludedProducts.Clear();

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
            profile.BaseRecommendedProducts.Clear();
            profile.BaseRecommendedProductIds.Clear();

            profile.LastLookupProductName = null;
            profile.LastLookupProductId = null;
            profile.LastSearchProductNames.Clear();
            profile.LastSearchProductIds.Clear();
            profile.LastResolvedProductId = null;
            profile.LastResolvedProductName = null;
            profile.LastLookupCandidateNames.Clear();

            profile.LastResolvedOrderId = null;
            profile.LastResolvedOrderPhone = null;

            profile.HasActiveRecommendationContext = false;
            profile.HasActiveCompareContext = false;

            profile.ActiveFlow = null;
            profile.LastAnswerMode = null;
            profile.LastComparisonFeature = null;
            profile.LastIntentType = null;
            profile.LastUserMessage = null;

            profile.LastResolvedBrandSwitchFrom = null;
            profile.LastResolvedBrandSwitchTo = null;
            profile.HasPendingOrderLookup = false;
            profile.PendingOrderId = null;
            profile.PendingOrderPhone = null;
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

            var compared = productNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            profile.LastComparedProducts.Clear();
            profile.LastComparedProducts.AddRange(compared);

            profile.HasActiveCompareContext = profile.LastComparedProducts.Count >= 2;

            if (profile.HasActiveCompareContext)
            {
                profile.ActiveFlow = ChatFlowType.Compare;

                profile.HasActiveRecommendationContext = false;
                profile.LastComparisonFeature = null;
            }

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
        private static void ApplyFreshRecommendationProfile(
    CustomerPreferenceProfile profile,
    ParsedIntent intent,
    bool messageExplicitlyMentionsMale,
    bool messageExplicitlyMentionsFemale)
        {
            // Reset các field recommendation-sensitive
            profile.PriceMin = null;
            profile.PriceMax = null;
            profile.TargetPrice = null;
            profile.FilterType = PriceFilterType.None;

            profile.PreferredBrand = null;
            profile.PreferredCategory = null;
            profile.Target = null;

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

            profile.ExcludedCategories.Clear();
            profile.ExcludedBrands.Clear();
            profile.RequestedStyles.Clear();
            profile.ExcludedProducts.Clear();

            // Gán lại từ intent mới
            if (intent.FilterType != PriceFilterType.None)
            {
                profile.FilterType = intent.FilterType;

                switch (intent.FilterType)
                {
                    case PriceFilterType.Range:
                        profile.PriceMin = intent.PriceMin;
                        profile.PriceMax = intent.PriceMax;
                        break;

                    case PriceFilterType.MaxOnly:
                        profile.PriceMax = intent.PriceMax;
                        break;

                    case PriceFilterType.MinOnly:
                        profile.PriceMin = intent.PriceMin;
                        break;

                    case PriceFilterType.Around:
                        profile.TargetPrice = intent.TargetPrice;
                        profile.PriceMin = intent.PriceMin;
                        profile.PriceMax = intent.PriceMax;
                        break;
                }
            }
            else
            {
                profile.PriceMin = intent.PriceMin;
                profile.PriceMax = intent.PriceMax;
                profile.TargetPrice = intent.TargetPrice;
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
                profile.PreferredCategory = intent.Category;

            if (!string.IsNullOrWhiteSpace(intent.Brand))
                profile.PreferredBrand = intent.Brand;

            if (!string.IsNullOrWhiteSpace(intent.Target))
                profile.Target = intent.Target;

            if (intent.HeightCm.HasValue)
                profile.HeightCm = intent.HeightCm;

            profile.NeedsLowSeat = intent.NeedsLowSeat;

            profile.ForSchool = intent.ForSchool;
            profile.ForWork = intent.ForWork;
            profile.ForCity = intent.ForCity;
            profile.ForTour = intent.ForTour;

            profile.WantsEasyControl = intent.WantsEasyControl;
            profile.WantsFuelSaving = intent.WantsFuelSaving;
            profile.WantsLargeStorage = intent.WantsLargeStorage;

            foreach (var item in intent.ExcludedCategories)
                profile.ExcludedCategories.Add(item);

            foreach (var item in intent.ExcludedBrands)
                profile.ExcludedBrands.Add(item);
            foreach (var item in intent.ExcludedProducts)
                profile.ExcludedProducts.Add(item);

            foreach (var style in intent.RequestedStyles)
                profile.RequestedStyles.Add(style);
            if (profile.ExcludedBrands.Any() &&
    !string.IsNullOrWhiteSpace(profile.PreferredBrand) &&
    profile.ExcludedBrands.Contains(profile.PreferredBrand))
            {
                profile.PreferredBrand = null;
            }
            if (messageExplicitlyMentionsMale)
            {
                profile.Target = "nam";
                profile.PrefersMaleStyle = true;
                profile.PrefersFemaleStyle = false;
            }
            else if (messageExplicitlyMentionsFemale)
            {
                profile.Target = "nữ";
                profile.PrefersFemaleStyle = true;
                profile.PrefersMaleStyle = false;
            }
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
            if (profile.ExcludedProducts.Count > 0)
                parts.Add($"không muốn mẫu: {string.Join(", ", profile.ExcludedProducts)}");
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

            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                parts.Add($"mẫu vừa tra cứu: {profile.LastLookupProductName}");
            if (!string.IsNullOrWhiteSpace(profile.LastResolvedProductName))
                parts.Add($"mẫu vừa resolve: {profile.LastResolvedProductName}");

            if (profile.LastLookupCandidateNames.Count > 0)
                parts.Add($"candidate lookup gần nhất: {string.Join(", ", profile.LastLookupCandidateNames.Take(5))}");

            if (profile.LastResolvedOrderId.HasValue)
                parts.Add($"đơn vừa resolve: DH{profile.LastResolvedOrderId.Value:D3}");

            if (!string.IsNullOrWhiteSpace(profile.LastResolvedOrderPhone))
                parts.Add($"số điện thoại đơn gần nhất: {profile.LastResolvedOrderPhone}");

            if (profile.LastSearchProductNames.Count > 0)
                parts.Add($"danh sách vừa lọc: {string.Join(", ", profile.LastSearchProductNames.Take(5))}");

            if (!string.IsNullOrWhiteSpace(profile.ActiveFlow))
                parts.Add($"flow hiện tại: {profile.ActiveFlow}");

            if (!string.IsNullOrWhiteSpace(profile.LastIntentType))
                parts.Add($"intent gần nhất: {profile.LastIntentType}");

            if (profile.HasActiveRecommendationContext)
                parts.Add("đang có ngữ cảnh gợi ý trước đó");

            if (profile.HasActiveCompareContext)
                parts.Add("đang có ngữ cảnh so sánh");

            if (!string.IsNullOrWhiteSpace(profile.LastAnswerMode))
                parts.Add($"kiểu trả lời gần nhất: {profile.LastAnswerMode}");

            if (profile.LastRecommendedProductIds.Count > 0)
                parts.Add($"ids vừa gợi ý: {string.Join(", ", profile.LastRecommendedProductIds)}");

            if (profile.LastLookupProductId.HasValue)
                parts.Add($"id vừa tra cứu: {profile.LastLookupProductId.Value}");

            if (profile.LastSearchProductIds.Count > 0)
                parts.Add($"ids vừa lọc: {string.Join(", ", profile.LastSearchProductIds.Take(10))}");

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
        public Task SetBaseRecommendedProductsAsync(
    string conversationId,
    IEnumerable<ProductSummaryDto> products)
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

            profile.BaseRecommendedProductIds = items
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            profile.BaseRecommendedProducts = items
                .Select(x => x.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.UpdatedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }
        private static string NormalizeGenderText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.Trim().ToLowerInvariant();

            value = value
                .Replace("đ", "d")
                .Replace("nữ", "nu");

            var map = new Dictionary<char, char>
            {
                ['à'] = 'a',
                ['á'] = 'a',
                ['ạ'] = 'a',
                ['ả'] = 'a',
                ['ã'] = 'a',
                ['â'] = 'a',
                ['ầ'] = 'a',
                ['ấ'] = 'a',
                ['ậ'] = 'a',
                ['ẩ'] = 'a',
                ['ẫ'] = 'a',
                ['ă'] = 'a',
                ['ằ'] = 'a',
                ['ắ'] = 'a',
                ['ặ'] = 'a',
                ['ẳ'] = 'a',
                ['ẵ'] = 'a',
                ['è'] = 'e',
                ['é'] = 'e',
                ['ẹ'] = 'e',
                ['ẻ'] = 'e',
                ['ẽ'] = 'e',
                ['ê'] = 'e',
                ['ề'] = 'e',
                ['ế'] = 'e',
                ['ệ'] = 'e',
                ['ể'] = 'e',
                ['ễ'] = 'e',
                ['ì'] = 'i',
                ['í'] = 'i',
                ['ị'] = 'i',
                ['ỉ'] = 'i',
                ['ĩ'] = 'i',
                ['ò'] = 'o',
                ['ó'] = 'o',
                ['ọ'] = 'o',
                ['ỏ'] = 'o',
                ['õ'] = 'o',
                ['ô'] = 'o',
                ['ồ'] = 'o',
                ['ố'] = 'o',
                ['ộ'] = 'o',
                ['ổ'] = 'o',
                ['ỗ'] = 'o',
                ['ơ'] = 'o',
                ['ờ'] = 'o',
                ['ớ'] = 'o',
                ['ợ'] = 'o',
                ['ở'] = 'o',
                ['ỡ'] = 'o',
                ['ù'] = 'u',
                ['ú'] = 'u',
                ['ụ'] = 'u',
                ['ủ'] = 'u',
                ['ũ'] = 'u',
                ['ư'] = 'u',
                ['ừ'] = 'u',
                ['ứ'] = 'u',
                ['ự'] = 'u',
                ['ử'] = 'u',
                ['ữ'] = 'u',
                ['ỳ'] = 'y',
                ['ý'] = 'y',
                ['ỵ'] = 'y',
                ['ỷ'] = 'y',
                ['ỹ'] = 'y'
            };

            var chars = value.Select(c => map.ContainsKey(c) ? map[c] : c).ToArray();
            value = new string(chars);

            while (value.Contains("  "))
            {
                value = value.Replace("  ", " ");
            }

            return value;

        }
        public Task SetSemanticContextAsync(string conversationId, SemanticResult semanticResult)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return Task.CompletedTask;
            }

            var profile = _store.GetOrAdd(conversationId, id => new CustomerPreferenceProfile
            {
                ConversationId = id
            });

            profile.SemanticResult = semanticResult;

            return Task.CompletedTask;
        }
    }
}