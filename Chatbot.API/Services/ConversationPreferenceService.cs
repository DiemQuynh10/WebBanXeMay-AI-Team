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
            var rawMessage = NormalizeGenderText(intent.RawMessage);

            bool messageExplicitlyMentionsMale =
                rawMessage.Contains(" nam ") ||
                rawMessage.StartsWith("nam ") ||
                rawMessage.EndsWith(" nam") ||
                rawMessage.Contains("cho nam") ||
                rawMessage.Contains("phai nam");

            bool messageExplicitlyMentionsFemale =
                rawMessage.Contains(" nu ") ||
                rawMessage.StartsWith("nu ") ||
                rawMessage.EndsWith(" nu") ||
                rawMessage.Contains("cho nu") ||
                rawMessage.Contains("phai nu") ||
                rawMessage.Contains("phu nu");
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

                    default:
                        break;
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
            else
            {
                if (intent.PriceMin.HasValue)
                    profile.PriceMin = intent.PriceMin;

                if (intent.PriceMax.HasValue)
                    profile.PriceMax = intent.PriceMax;

                if (intent.TargetPrice.HasValue)
                    profile.TargetPrice = intent.TargetPrice;
            }

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

            if (messageExplicitlyMentionsMale && intent.PrefersMaleStyle && !intent.PrefersFemaleStyle)
            {
                profile.PrefersMaleStyle = true;
                profile.PrefersFemaleStyle = false;
            }
            else if (messageExplicitlyMentionsFemale && intent.PrefersFemaleStyle && !intent.PrefersMaleStyle)
            {
                profile.PrefersFemaleStyle = true;
                profile.PrefersMaleStyle = false;
            }

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

            if (!string.IsNullOrWhiteSpace(intent.RouteFlow) &&
                !string.Equals(intent.RouteFlow, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                profile.ActiveFlow = intent.RouteFlow;
            }

            if (intent.IsDirectCompare)
            {
                profile.HasActiveCompareContext = true;
            }

            profile.TurnCount++;
            profile.LastUserMessage = intent.RawMessage;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            Console.WriteLine(
    $"[ConversationPreferenceService] Merge result | RawMessage={intent.RawMessage} | " +
    $"messageExplicitlyMentionsMale={messageExplicitlyMentionsMale} | " +
    $"messageExplicitlyMentionsFemale={messageExplicitlyMentionsFemale} | " +
    $"intent.Target={intent.Target} | " +
    $"intent.PrefersMaleStyle={intent.PrefersMaleStyle} | " +
    $"intent.PrefersFemaleStyle={intent.PrefersFemaleStyle} | " +
    $"profile.Target={profile.Target} | " +
    $"profile.PrefersMaleStyle={profile.PrefersMaleStyle} | " +
    $"profile.PrefersFemaleStyle={profile.PrefersFemaleStyle}");
            return Task.FromResult(profile);
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

            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                parts.Add($"mẫu vừa tra cứu: {profile.LastLookupProductName}");

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
    }
}