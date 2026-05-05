using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationFollowUpService : IRecommendationFollowUpService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<RecommendationFollowUpService> _logger;

        public RecommendationFollowUpService(
            IWebBanXeMayToolClient toolClient,
            IProductRecommendationService productRecommendationService,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<RecommendationFollowUpService> logger)
        {
            _toolClient = toolClient;
            _productRecommendationService = productRecommendationService;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }
            public async Task<ChatResponse?> HandleAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            bool isPickBest =
    string.Equals(intent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase);

            if (!profile.HasActiveRecommendationContext && !isPickBest)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because recommendation context is inactive. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }

            var sourceNames =
     profile.BaseRecommendedProducts != null && profile.BaseRecommendedProducts.Count >= 2
         ? profile.BaseRecommendedProducts
         : profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count >= 2
             ? profile.LastRecommendedProducts
             : profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count >= 2
                 ? profile.CurrentRecommendedProducts
                 : profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count >= 2
                     ? profile.LastMentionedProducts
                     : profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0
                         ? profile.CurrentRecommendedProducts
                         : profile.LastComparedProducts;

            if (sourceNames == null || sourceNames.Count == 0)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because no recommended products available. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }

            var allowedNames = sourceNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Follow-up rerank started. ConversationId: {ConversationId}, AllowedProducts: {AllowedProducts}",
                conversationId,
                string.Join(", ", allowedNames));

            var products = new List<ProductSummaryDto>();
           
            foreach (var name in allowedNames)
            {
                var result = await _toolClient.SearchProductsAsync(name, 3);

                var matched = result?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (matched != null)
                {
                    products.Add(matched);
                }
                else
                {
                    _logger.LogInformation(
                        "Follow-up rerank could not find product by name. ConversationId: {ConversationId}, ProductName: {ProductName}",
                        conversationId,
                        name);
                }
            }

            products = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten) && allowedNames.Contains(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            products = ProductExclusionHelper.ApplyExclusions(products, intent, profile);

            _logger.LogInformation(
                "Follow-up rerank candidates after strict filter. ConversationId: {ConversationId}, Count: {Count}, Products: {Products}",
                conversationId,
                products.Count,
                string.Join(", ", products.Select(x => x.Ten)));


            if (string.Equals(intent.FollowUpType, "cheapest_in_list", StringComparison.OrdinalIgnoreCase))
            {
                if (products == null || products.Count == 0)
                    return null;

                var cheapestItems = products
                                .OrderBy(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .Take(3)
                    .ToList();

                if (cheapestItems.Count == 0)
                    return null;

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    cheapestItems,
                    "followup");

                var cheapest = cheapestItems.First();

                var samePriceItems = cheapestItems
                    .Where(x => x.Gia == cheapest.Gia)
                    .ToList();

                string cheapestReply;

                if (samePriceItems.Count > 1)
                {
                    cheapestReply =
                        $"Trong các mẫu vừa gợi ý, mức rẻ nhất hiện là **{cheapest.Gia:N0} VNĐ**.\n\n" +
                        "Các mẫu cùng mức giá thấp nhất là:\n" +
                        string.Join("\n", samePriceItems.Select(x => $"- **{x.Ten}** ({x.Gia:N0} VNĐ)"));
                }
                else
                {
                    cheapestReply =
                        $"Trong các mẫu vừa gợi ý, mẫu rẻ nhất là **{cheapest.Ten}** với giá khoảng **{cheapest.Gia:N0} VNĐ**.";
                }

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = cheapestReply,
                    Products = ChatProductCardMapper.MapMany(samePriceItems, 3)
                };
            }

            if (products.Count == 0)
            {
                if (HasExclusionIntent(intent))
                {
                    _logger.LogInformation(
                        "Follow-up exclusion emptied old recommendation list. Loading broad candidates. ConversationId: {ConversationId}",
                        conversationId);

                    products = await LoadBroadCandidatesForExclusionAsync();
                    products = ProductExclusionHelper.ApplyExclusions(products, intent, profile);
                }

                if (products.Count == 0)
                {
                    return null;
                }
            }
            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                products = products
                    .Where(x => string.Equals(x.ThuongHieu, intent.Brand, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                products = products
                    .Where(x => (x.Loai ?? string.Empty).Contains(intent.Category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (intent.ExcludedBrands.Any())
            {
                products = products
                    .Where(x => !intent.ExcludedBrands.Any(ex =>
                        string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            if (intent.ExcludedCategories.Any())
            {
                products = products
                    .Where(x => !intent.ExcludedCategories.Any(ex =>
                        (x.Loai ?? string.Empty).Contains(ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            products = ProductExclusionHelper.ApplyExclusions(products, intent, profile);

            if (products.Count == 0)
            {
                if (HasExclusionIntent(intent))
                {
                    _logger.LogInformation(
                        "Follow-up candidates empty after exclusions. Reloading broad candidates. ConversationId: {ConversationId}",
                        conversationId);

                    products = await LoadBroadCandidatesForExclusionAsync();
                    products = ProductExclusionHelper.ApplyExclusions(products, intent, profile);
                }

                if (products.Count == 0)
                {
                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply = BuildNoMatchReply(intent)
                    };
                }
            }
            bool hasExplicitPriceRefinement =
     intent.FilterType == PriceFilterType.MaxOnly ||
     intent.FilterType == PriceFilterType.MinOnly ||
     intent.FilterType == PriceFilterType.Range ||
     intent.FilterType == PriceFilterType.Around;

            if (hasExplicitPriceRefinement)
            {
                products = ProductPriceFilterHelper.ApplyStrictPriceFilter(products, intent);
            }

            if (products.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = BuildNoMatchReply(intent)
                };
            }
            bool wantsAlternative =
    normalizedMessage.Contains("mau khac") ||
    normalizedMessage.Contains("xe khac") ||
    normalizedMessage.Contains("khac di") ||
    normalizedMessage.Contains("doi mau khac") ||
    normalizedMessage.Contains("con mau nao khac");

            if (wantsAlternative && profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0)
            {
                var currentTop = profile.CurrentRecommendedProducts.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(currentTop))
                {
                    var filteredAlternatives = products
                        .Where(x => !string.Equals(x.Ten, currentTop, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (filteredAlternatives.Count > 0)
                    {
                        products = filteredAlternatives;
                    }
                }
            }
            products = ApplyFollowUpSemanticOrdering(products, intent, normalizedMessage);
            var reranked = _productRecommendationService.RankProducts(
                            products,
                intent,
                profile,
                normalizedMessage,
                take: Math.Min(4, products.Count));

            if (reranked == null || reranked.Count == 0)
            {
                return null;
            }
            if (string.Equals(intent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase))
            {
                var best = PickBestByFeature(products, reranked, normalizedMessage);
                string reason;

                var text = NormalizeText(normalizedMessage);

                bool isFemale =
                    intent.PrefersFemaleStyle ||
                    (!string.IsNullOrWhiteSpace(intent.Target) &&
                     (NormalizeText(intent.Target).Contains("nu"))) ||
                    text.Contains("nu");

                if (isFemale)
                {
                    reason = $"{best.Ten} hợp nữ hơn trong nhóm này vì dáng xe gọn, dễ điều khiển và phù hợp đi phố.";
                }
                else if (ContainsAny(text, "tiet kiem", "tiet kiem xang", "it hao xang", "hao xang it", "an xang it"))
                {
                    reason = BuildPickBestReasonByFeature(best, "fuel_saving");
                }
                else if (ContainsAny(text, "ben", "do ben", "it hong", "bao duong", "de bao duong", "dung lau"))
                {
                    reason = BuildPickBestReasonByFeature(best, "durability");
                }
                else if (ContainsAny(text, "di xa", "duong dai", "di duong dai", "chay xa", "di lau"))
                {
                    reason = BuildPickBestReasonByFeature(best, "long_distance");
                }
                else if (ContainsAny(text, "manh", "khoe", "may khoe", "boc"))
                {
                    reason = $"{best.Ten} có động cơ mạnh hơn và cảm giác lái đầm hơn.";
                }
                else
                {
                    reason = $"{best.Ten} đang cân bằng tốt giữa giá, độ dễ dùng và nhu cầu hằng ngày.";
                }
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply =
                        $"Nếu phải chọn 1 mẫu phù hợp nhất trong nhóm vừa gợi ý, mình sẽ chốt **{best.Ten}**.\n\n" +
                        $"Lý do: {reason} (giá khoảng **{best.Gia:N0} VNĐ**).\n\n" +
                        $"Bạn có thể bấm xem chi tiết mẫu này để kiểm tra thêm trước khi quyết định.",
                    Products = new List<ChatProductCard>
    {
        ChatProductCardMapper.Map(best)
    }
                };
            }

            var reply = BuildReply(reranked, intent, normalizedMessage, _productRecommendationService);
            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
    conversationId,
    reranked,
    "followup");
            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(reranked, 4)
            };
        }
        private static string BuildReply(
     IReadOnlyList<ProductSummaryDto> items,
     ParsedIntent intent,
     string normalizedMessage,
     IProductRecommendationService productRecommendationService)
        {
            var top = items[0];
            var backups = items.Skip(1).Take(2).ToList();
            bool wantsAlternativeTone =
    !string.IsNullOrWhiteSpace(normalizedMessage) &&
    (
        normalizedMessage.Contains("mau khac") ||
        normalizedMessage.Contains("xe khac") ||
        normalizedMessage.Contains("khac di") ||
        normalizedMessage.Contains("doi mau khac") ||
        normalizedMessage.Contains("con mau nao khac")
    );
            var focusLabel = intent.ComparisonFeature switch
            {
                "storage" => "cốp rộng",
                "low_seat" => "dễ chống chân",
                "fuel_saving" => "tiết kiệm xăng",
                "female_fit" => "hợp nữ",
                "work_fit" => "thực dụng / đi làm hằng ngày",
                "school_fit" => "đi học / sinh viên",
                "ride_comfort" => "đi êm",
                _ => null
            };
            if (focusLabel == null)
            {
                if (intent.WantsLargeStorage) focusLabel = "cốp rộng";
                else if (intent.NeedsLowSeat || intent.WantsEasyControl) focusLabel = "dễ chống chân";
                else if (intent.WantsFuelSaving) focusLabel = "tiết kiệm xăng";
                else if (intent.PrefersFemaleStyle || (!string.IsNullOrWhiteSpace(intent.Target) && intent.Target.Contains("nữ")))
                    focusLabel = "hợp nữ";
                else if (intent.ForWork) focusLabel = "đi làm hằng ngày";
                else if (intent.ForSchool) focusLabel = "đi học / sinh viên";
            }
            var topReason = productRecommendationService.BuildMainReason(top, intent);

            string intro;

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu giữ mức **dưới {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu xét các mẫu **từ {intent.PriceMin.Value:N0} VNĐ trở lên** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.Range &&
                     intent.PriceMin.HasValue &&
                     intent.PriceMax.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu lọc trong khoảng **{intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu ưu tiên quanh mức **{intent.TargetPrice.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else
            {
                if (wantsAlternativeTone)
                {
                    intro = $"Nếu đổi sang một phương án khác trong nhóm vừa rồi thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
                }
                else
                {
                    intro = focusLabel switch
                    {
                        "cốp rộng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "dễ chống chân" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "tiết kiệm xăng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "hợp nữ" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "thực dụng / đi làm hằng ngày" => $"Nếu chọn trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "đi học / sinh viên" => $"Nếu xét trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        "đi êm" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                        _ => $"Trong các mẫu vừa rồi, mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}."
                    };
                }
                intro = focusLabel switch
                {
                    "cốp rộng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "dễ chống chân" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "tiết kiệm xăng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "hợp nữ" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "thực dụng / đi làm hằng ngày" => $"Nếu chọn trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "đi học / sinh viên" => $"Nếu xét trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "đi êm" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    _ => $"Trong các mẫu vừa rồi, mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}."
                };
            }

            var sb = new StringBuilder();
            sb.AppendLine(intro);

            if (backups.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Các phương án phụ bạn vẫn có thể cân nhắc thêm:");

                foreach (var item in backups)
                {
                    var reason = productRecommendationService.BuildMainReason(item, intent);
                    sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {reason}");
                }
            }

            return sb.ToString().Trim();
        }
        private static bool HasExclusionIntent(ParsedIntent intent)
        {
            return intent.ExcludedBrands.Any() ||
                   intent.ExcludedProducts.Any() ||
                   intent.ExcludedCategories.Any();
        }
        private async Task<List<ProductSummaryDto>> LoadBroadCandidatesForExclusionAsync()
        {
            var result = await _toolClient.GetProductsByFiltersAsync(
    brand: null,
    minPrice: null,
    maxPrice: null,
    category: null,
    take: 200);

            return result?.Items?
                .Where(x => x != null)
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList()
                ?? new List<ProductSummaryDto>();
        }
        private static string BuildNoMatchReply(ParsedIntent intent)
        {
            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm **dưới {intent.PriceMax.Value:N0} VNĐ**.";
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm **từ {intent.PriceMin.Value:N0} VNĐ trở lên**.";
            }

            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm trong khoảng **{intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ**.";
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự đủ sát mức **khoảng {intent.TargetPrice.Value:N0} VNĐ**.";
            }

            return "Trong nhóm mình vừa gợi ý, hiện chưa còn mẫu nào thật sự phù hợp với tiêu chí này.";
        }
        private static List<ProductSummaryDto> ApplyFollowUpSemanticOrdering(
    List<ProductSummaryDto> items,
    ParsedIntent intent,
    string normalizedMessage)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            bool prefersFemale =
    intent.PrefersFemaleStyle ||
    (!string.IsNullOrWhiteSpace(intent.Target) &&
     (intent.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase) ||
      intent.Target.Contains("nu", StringComparison.OrdinalIgnoreCase)));

            if (prefersFemale || text.Contains("hop nu") || text.Contains("cho nu"))
            {
                return items
                    .OrderByDescending(x => LooksLikeFemaleFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.ForWork || text.Contains("di lam"))
            {
                return items
                    .OrderByDescending(x => LooksLikeWorkFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.ForSchool || text.Contains("di hoc"))
            {
                return items
                    .OrderByDescending(x => LooksLikeSchoolFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }
            if (intent.WantsLargeStorage || text.Contains("cop rong"))
            {
                return items
                    .OrderByDescending(x => LooksLikeLargeStorage(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.WantsFuelSaving || text.Contains("tiet kiem xang"))
            {
                return items
                    .OrderByDescending(x => LooksLikeFuelSaving(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.NeedsLowSeat || intent.WantsEasyControl || text.Contains("de chong chan"))
            {
                return items
                    .OrderByDescending(x => LooksLikeLowSeat(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }
            if (text.Contains("di xa") ||
    text.Contains("duong dai") ||
    text.Contains("di duong dai") ||
    text.Contains("chay xa"))
            {
                return items
                    .OrderByDescending(x => LooksLikeLongDistanceFriendly(x))
                    .ThenByDescending(x => x.Gia)
                    .ToList();
            }
            return items;
        }
        private static bool LooksLikeLargeStorage(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("latte") || name.Contains("lead") || name.Contains("freego") || name.Contains("air blade");
        }

        private static bool LooksLikeFuelSaving(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("wave") || name.Contains("future") || name.Contains("sirius");
        }

        private static bool LooksLikeLowSeat(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("janus") || name.Contains("zip") || name.Contains("latte");
        }
        private static bool LooksLikeFemaleFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("latte") ||
                   name.Contains("janus") ||
                   name.Contains("zip") ||
                   name.Contains("lead");
        }

        private static bool LooksLikeWorkFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("future") ||
                   name.Contains("wave") ||
                   name.Contains("air blade") ||
                   name.Contains("freego");
        }

        private static bool LooksLikeSchoolFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("janus") ||
                   name.Contains("zip") ||
                   name.Contains("sirius") ||
                   name.Contains("wave");
        }
        private static bool LooksLikeLongDistanceFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();

            return name.Contains("air blade") ||
                   name.Contains("winner") ||
                   name.Contains("exciter") ||
                   name.Contains("freego") ||
                   name.Contains("future") ||
                   name.Contains("jupiter") ||
                   name.Contains("impulse") ||
                   name.Contains("axelo") ||
                   name.Contains("gd110");
        }
        private static string BuildPickBestReasonByFeature(ProductSummaryDto product, string feature)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            var category = (product.Loai ?? string.Empty).ToLowerInvariant();

            if (feature == "fuel_saving")
            {
                if (name.Contains("wave") || name.Contains("sirius") || name.Contains("future") || name.Contains("jupiter"))
                    return $"{product.Ten} hợp hơn nếu ưu tiên tiết kiệm xăng vì là nhóm xe số phổ thông, máy nhỏ, chi phí vận hành thấp và dễ dùng hằng ngày";

                if (name.Contains("vision") || name.Contains("address") || name.Contains("freego") || name.Contains("janus"))
                    return $"{product.Ten} hợp hơn nếu ưu tiên tiết kiệm xăng vì xe khá gọn, dung tích vừa phải, phù hợp đi phố và chi phí sử dụng dễ chịu";

                return $"{product.Ten} hợp hơn nếu ưu tiên tiết kiệm xăng vì chi phí sử dụng tương đối dễ chịu trong nhóm vừa gợi ý";
            }

            if (feature == "durability")
            {
                if (name.Contains("honda"))
                    return $"{product.Ten} hợp hơn nếu ưu tiên độ bền vì Honda phổ biến, dễ bảo dưỡng và phụ tùng dễ tìm";

                if (name.Contains("wave") || name.Contains("future") || name.Contains("sirius") || name.Contains("jupiter") || name.Contains("smash"))
                    return $"{product.Ten} hợp hơn nếu ưu tiên độ bền vì thuộc nhóm xe phổ thông, kết cấu đơn giản, dễ sửa và dễ bảo dưỡng";

                return $"{product.Ten} hợp hơn nếu ưu tiên độ bền vì mẫu này khá thực dụng, dễ dùng lâu dài và chi phí bảo dưỡng không quá cao";
            }
            if (feature == "long_distance")
            {
                if (name.Contains("air blade") || name.Contains("freego"))
                    return $"{product.Ten} hợp đi xa hơn vì xe đầm hơn nhóm xe nhỏ, tư thế ngồi thoải mái hơn và máy đủ khỏe cho quãng đường dài";

                if (name.Contains("future") || name.Contains("jupiter") || name.Contains("impulse") || name.Contains("axelo") || name.Contains("gd110"))
                    return $"{product.Ten} hợp đi xa hơn trong nhóm này vì dáng xe ổn định hơn, máy bền và phù hợp chạy quãng đường dài hơn các mẫu quá nhỏ gọn";

                return $"{product.Ten} hợp đi xa hơn trong nhóm vừa gợi ý vì tổng thể ổn định, dễ kiểm soát và phù hợp chạy lâu hơn";
            }

            return $"{product.Ten} đang cân bằng tốt giữa giá, độ dễ dùng và nhu cầu hằng ngày";
        }
        private static ProductSummaryDto PickBestByFeature(
    List<ProductSummaryDto> semanticallyOrderedProducts,
    IReadOnlyList<ProductSummaryDto> rerankedProducts,
    string normalizedMessage)
        {
            var text = NormalizeText(normalizedMessage);

            if (ContainsAny(text, "di xa", "duong dai", "di duong dai", "chay xa", "di lau"))
            {
                var bestLongDistance = semanticallyOrderedProducts.FirstOrDefault(LooksLikeLongDistanceFriendly);
                if (bestLongDistance != null)
                    return bestLongDistance;
            }

            if (ContainsAny(text, "tiet kiem", "tiet kiem xang", "it hao xang", "hao xang it", "an xang it"))
            {
                var bestFuelSaving = semanticallyOrderedProducts.FirstOrDefault(LooksLikeFuelSaving);
                if (bestFuelSaving != null)
                    return bestFuelSaving;
            }

            if (ContainsAny(text, "ben", "do ben", "it hong", "bao duong", "de bao duong", "dung lau"))
            {
                var bestDurable = semanticallyOrderedProducts.FirstOrDefault(x =>
                    ContainsAny(x.Ten, "Honda", "Vision", "Future", "Wave", "Jupiter", "Sirius", "Impulse"));

                if (bestDurable != null)
                    return bestDurable;
            }

            return rerankedProducts.First();
        }
        private static bool ContainsAny(string? text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
                return false;

            return keywords.Any(k =>
                !string.IsNullOrWhiteSpace(k) &&
                text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim().ToLowerInvariant();

            text = text
                .Replace('à', 'a').Replace('á', 'a').Replace('ạ', 'a').Replace('ả', 'a').Replace('ã', 'a')
                .Replace('â', 'a').Replace('ầ', 'a').Replace('ấ', 'a').Replace('ậ', 'a').Replace('ẩ', 'a').Replace('ẫ', 'a')
                .Replace('ă', 'a').Replace('ằ', 'a').Replace('ắ', 'a').Replace('ặ', 'a').Replace('ẳ', 'a').Replace('ẵ', 'a')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẹ', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e')
                .Replace('ê', 'e').Replace('ề', 'e').Replace('ế', 'e').Replace('ệ', 'e').Replace('ể', 'e').Replace('ễ', 'e')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ị', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ọ', 'o').Replace('ỏ', 'o').Replace('õ', 'o')
                .Replace('ô', 'o').Replace('ồ', 'o').Replace('ố', 'o').Replace('ộ', 'o').Replace('ổ', 'o').Replace('ỗ', 'o')
                .Replace('ơ', 'o').Replace('ờ', 'o').Replace('ớ', 'o').Replace('ợ', 'o').Replace('ở', 'o').Replace('ỡ', 'o')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ụ', 'u').Replace('ủ', 'u').Replace('ũ', 'u')
                .Replace('ư', 'u').Replace('ừ', 'u').Replace('ứ', 'u').Replace('ự', 'u').Replace('ử', 'u').Replace('ữ', 'u')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỵ', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y')
                .Replace('đ', 'd');

            return text;
        }
    }

}