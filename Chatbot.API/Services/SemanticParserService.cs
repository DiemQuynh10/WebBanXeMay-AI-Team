using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chatbot.API.Configurations;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Services
{
    public class SemanticParserService : ISemanticParserService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly OpenAISettings _settings;
        private readonly ILogger<SemanticParserService> _logger;

        public SemanticParserService(
            HttpClient httpClient,
            IOptions<OpenAISettings> settings,
            ILogger<SemanticParserService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<SemanticResult> ParseAsync(
            string rawMessage,
            CustomerPreferenceProfile? profile = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
                throw new ArgumentException("Message cannot be empty.", nameof(rawMessage));

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.openai.com/v1/chat/completions");

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                request.Content = new StringContent(
                    BuildOpenAiRequestBody(rawMessage.Trim(), profile),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Semantic parser OpenAI call failed. StatusCode={(int)response.StatusCode}, Body={rawResponse}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(rawResponse, JsonOptions);
                var content = completion?.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException("Semantic parser received an empty completion content.");

                var semantic = JsonSerializer.Deserialize<SemanticResult>(content, JsonOptions);
                if (semantic == null)
                    throw new JsonException("Failed to deserialize SemanticResult from OpenAI content.");

                return NormalizeResult(semantic, rawMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SemanticParserService failed. Returning heuristic fallback semantic result.");
                return BuildHeuristicFallback(rawMessage);
            }
        }

        private string BuildOpenAiRequestBody(string rawMessage, CustomerPreferenceProfile? profile)
        {
            var request = new
            {
                model = _settings.Model,
                temperature = 0.0,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt(profile)
                    },
                    new
                    {
                        role = "user",
                        content = rawMessage
                    }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "semantic_result",
                        strict = true,
                        schema = BuildJsonSchema()
                    }
                }
            };

            return JsonSerializer.Serialize(request);
        }

        private static object BuildJsonSchema()
        {
            return new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    intent = EnumStringProperty(
                        "Canonical user intent.",
                        "product_lookup",
                        "product_search",
                        "recommend",
                        "compare",
                        "refine",
                        "service_info",
                        "policy_info",
                        "greeting",
                        "order_lookup",
                        "out_of_scope",
                        "unknown"),
                    flow_type = EnumStringProperty(
                        "Internal flow to execute.",
                        ChatFlowType.ProductLookup,
                        ChatFlowType.ProductSearch,
                        ChatFlowType.Recommendation,
                        ChatFlowType.Compare,
                        ChatFlowType.Refinement,
                        ChatFlowType.Greeting,
                        ChatFlowType.OrderLookup,
                        ChatFlowType.OutOfScope,
                        ChatFlowType.Unknown),
                    brand = NullableStringProperty(),
                    category = NullableStringProperty(),
                    target = NullableStringProperty(),
                    price_min = NullableNumberProperty(),
                    price_max = NullableNumberProperty(),
                    target_price = NullableNumberProperty(),
                    price_filter_type = EnumStringProperty("Price filter type.", "none", "max_only", "min_only", "range", "around"),
                    price_level = EnumStringProperty("Implicit or explicit price level.", "thap", "trung_binh", "cao", "khong_ro"),
                    installment = EnumStringProperty("Whether installment payment is wanted.", "co", "khong", "khong_ro"),
                    interest = EnumStringProperty("Preferred installment interest level.", "thap", "trung_binh", "cao", "khong_ro"),
                    preference = StringProperty("Short natural-language preference summary."),
                    for_work = BoolProperty(),
                    for_school = BoolProperty(),
                    for_city = BoolProperty(),
                    for_tour = BoolProperty(),
                    wants_fuel_saving = BoolProperty(),
                    wants_large_storage = BoolProperty(),
                    wants_easy_control = BoolProperty(),
                    needs_low_seat = BoolProperty(),
                    height_cm = NullableIntegerProperty(),
                    mentioned_products = StringArrayProperty(),
                    excluded_brands = StringArrayProperty(),
                    excluded_categories = StringArrayProperty(),
                    excluded_products = StringArrayProperty(),
                    requested_styles = StringArrayProperty(),
                    comparison_feature = NullableStringProperty(),
                    lookup_field = NullableStringProperty(),
                    policy_slot = NullableStringProperty(),
                    normalized_meaning = StringProperty("Rewrite of the user's meaning in clear Vietnamese.")
                },
                required = new[]
                {
                    "intent",
                    "flow_type",
                    "brand",
                    "category",
                    "target",
                    "price_min",
                    "price_max",
                    "target_price",
                    "price_filter_type",
                    "price_level",
                    "installment",
                    "interest",
                    "preference",
                    "for_work",
                    "for_school",
                    "for_city",
                    "for_tour",
                    "wants_fuel_saving",
                    "wants_large_storage",
                    "wants_easy_control",
                    "needs_low_seat",
                    "height_cm",
                    "mentioned_products",
                    "excluded_brands",
                    "excluded_categories",
                    "excluded_products",
                    "requested_styles",
                    "comparison_feature",
                    "lookup_field",
                    "policy_slot",
                    "normalized_meaning"
                }
            };
        }

        private static string BuildSystemPrompt(CustomerPreferenceProfile? profile)
        {
            var prompt = """
You are the semantic understanding layer for a Vietnamese motorbike sales chatbot.

Return exactly one JSON object that matches the schema. Do not answer the user.

Core rules:
- Infer meaning from the whole utterance and conversation-style phrasing. Do not do keyword matching.
- Understand explicit negation and implicit negation.
- You must understand the full sentence meaning before assigning any slot. Never decide from one isolated word alone.
- If the sentence contains connectors like "nhung", "ma", "con", "van", treat them as additive constraints unless the user is explicitly correcting a previous condition.
- Do not drop any user constraint. Every important condition in the sentence must be reflected in slots or in preference.
- Put rejected brands/categories/products into excluded_* fields, not into selected brand/category/product fields.
- Negation applies to every entity type, not just brands.
- Phrases like "khong phai X", "khong X", "dung goi y X", "tru X", "ne X", "bo X", "xe khac khong phai X" mean X must go into the matching excluded_* field when X is a brand/category/product.
- If an entity appears only after a negation phrase, never put it into selected brand/category/target/mentioned_products.
- If the user negates a target/style such as "khong phai nu", "khong can xe the thao", clear that target/style unless a new positive replacement is explicitly stated.
- If the user negates a service/constraint such as "khong tra gop", set installment = khong and do not treat installment as a buying constraint.
- If the user corrects a previous constraint, normalized_meaning must express the corrected meaning only.
- normalized_meaning must be exactly one short Vietnamese sentence, clear and explicit, ideally under 18 words.
- normalized_meaning must convert implied meaning into explicit meaning and remove ambiguity.
- Use null for unavailable scalar slots, empty arrays for unavailable list slots, and khong_ro/none for unclear categorical slots.
- Do not mechanically copy slang words into slots; translate them into normalized semantic constraints.
- Distinguish vehicle recommendation requests from store service or policy questions.
- If the user asks whether the shop offers installment, maintenance, repair, warranty, or asks about store policy/process, do not map that to product recommendation.
- For short follow-up policy/service questions, reuse last_semantic_meaning and make normalized_meaning explicit.
- If the new utterance is only a slot such as "lai suat", "thoi gian", "can gi", "bao lau", "phi sao", combine it with the previous service/policy topic.
- normalized_meaning must include the domain and slot for policy/service questions, for example "hoi lai suat tra gop qua ngan hang", not just "hoi lai suat".
- For policy/service questions, also set policy_slot to the most specific slot when it is clear. Use values like documents, conditions, process, approval_time, interest, down_payment, refund, deposit, warranty_period, warranty_exclusion, maintenance_schedule, fee, registration_time.
- Example: "hồ sơ trả góp cần những gì" => policy_slot = "documents".

Semantic labels:
- intent must be one of: product_lookup, product_search, recommend, compare, refine, service_info, policy_info, greeting, order_lookup, out_of_scope, unknown.
- flow_type must be one of: product_lookup, product_search, recommendation, compare, refinement, greeting, order_lookup, out_of_scope, unknown.
- price_filter_type must be one of: none, max_only, min_only, range, around.
- price_level must be one of: thap, trung_binh, cao, khong_ro.
- installment must be one of: co, khong, khong_ro.
- interest must be one of: thap, trung_binh, cao, khong_ro.
- lookup_field may be: price, stock, cc, detail, or null.
- comparison_feature may be: storage, fuel_saving, low_seat, style, price, ride_comfort, work_fit, school_fit, or null.

Vietnamese semantic guidance:
- "gia mem", "binh dan", "re re", "nhe vi", "vua tui tien" usually mean price_level = thap.
- "gia vua phai", "tam trung", "khong qua dat" usually mean price_level = trung_binh unless the user asks explicitly for the cheapest option.
- "khong can xe xin", "khong can xe sang", "khong can xe cao cap" means the user rejects premium/expensive positioning. Use price_level = trung_binh, preference = "khong uu tien xe cao cap"; do not set price_level = cao.
- "dung goi y xe qua cao cap", "khong lay xe dat dau" means exclude premium/expensive suggestions and prefer affordable or mid-range choices.
- "tra gop nhe", "gop nhe", "lai nhe", "lai suat nhe" means installment = co and interest = thap.
- "toi khong muon tra gop", "khong can tra gop" means installment = khong, not co.
- "khong Honda/Yamaha", "tru xe so", "dung goi y Vision" are exclusions.
- "shop co tra gop khong", "bao duong co khong", "bao hanh the nao" are not vehicle recommendation requests.
- Use intent = service_info when the user asks whether a service exists or is available.
- Use intent = policy_info when the user asks about interest rate, paperwork, process, conditions, warranty policy, or store policy.
- For service_info and policy_info, set flow_type = unknown so the orchestration layer answers through knowledge/RAG instead of recommendation.
- Only use recommend when installment is a buying constraint for choosing a vehicle, for example "muon mua xe tra gop nhe".
- If the user says "xe re nhung phai ben", keep both constraints: price_level = thap and preference should mention durability.
- If the user says a complex sentence with multiple conditions, preference should summarize the non-schema conditions that still matter, for example "uu tien ben", "uu tien de di lam", "khong uu tien xe cao cap".

Examples:
Input: "gia mem la duoc"
Output intent = "product_search", flow_type = "product_search", price_level = "thap", normalized_meaning = "muon xe gia re"

Input: "xe binh dan thoi"
Output intent = "recommend", flow_type = "recommendation", price_level = "thap", normalized_meaning = "muon duoc goi y xe binh dan gia re"

Input: "khong can xe xin dau"
Output intent = "recommend", flow_type = "recommendation", price_level = "trung_binh", preference = "khong uu tien xe cao cap", normalized_meaning = "muon xe tam trung, khong can xe cao cap"

Input: "tra gop nhe thoi"
Output intent = "recommend", flow_type = "recommendation", installment = "co", interest = "thap", normalized_meaning = "muon mua xe tra gop voi lai suat thap"

Input: "toi khong muon tra gop"
Output installment = "khong", interest = "khong_ro", normalized_meaning = "muon mua xe khong tra gop"

Input: "tu van xe khac khong phai honda"
Output intent = "recommend", flow_type = "recommendation", brand = null, excluded_brands = ["Honda"], normalized_meaning = "muon duoc goi y xe khac, khong phai Honda"

Input: "tu van xe ga nhung khong vision"
Output intent = "recommend", flow_type = "recommendation", category = "xe ga", mentioned_products = [], excluded_products = ["Vision"], normalized_meaning = "muon xe ga nhung khong phai Vision"

Input: "khong phai xe ga nua, tu van xe so"
Output intent = "recommend", flow_type = "recommendation", category = "xe so", excluded_categories = ["xe ga"], normalized_meaning = "muon xe so, khong phai xe ga"

Input: "xe re nhung phai ben"
Output intent = "recommend", flow_type = "recommendation", price_level = "thap", preference = "uu tien do ben", normalized_meaning = "muon xe gia re va ben"

Input: "xe binh dan thoi"
Output normalized_meaning = "muon xe gia re"

Input: "shop co tra gop khong"
Output intent = "service_info", flow_type = "unknown", normalized_meaning = "hoi shop co ho tro tra gop khong"

Input: "bao duong xe co khong"
Output intent = "service_info", flow_type = "unknown", normalized_meaning = "hoi shop co dich vu bao duong xe khong"

Input: "tra gop can gi"
Output intent = "policy_info", flow_type = "unknown", normalized_meaning = "hoi thu tuc va giay to can cho tra gop"

Input: "bao hanh the nao"
Output intent = "policy_info", flow_type = "unknown", normalized_meaning = "hoi chinh sach bao hanh cua cua hang"

Context last_semantic_meaning: "hoi tra gop qua ngan hang"
Input: "lai suat"
Output intent = "policy_info", flow_type = "unknown", normalized_meaning = "hoi lai suat tra gop qua ngan hang"

Context last_semantic_meaning: "hoi chinh sach bao hanh Honda"
Input: "bao lau"
Output intent = "policy_info", flow_type = "unknown", brand = "Honda", normalized_meaning = "hoi thoi han bao hanh Honda"
""";

            if (profile == null)
            {
                return prompt + "\nConversation context: none.";
            }

            var context = BuildConversationContext(profile);
            return $"{prompt}\nConversation context:\n{context}";
        }

        private static string BuildConversationContext(CustomerPreferenceProfile profile)
        {
            var lines = new List<string>
            {
                $"- active_flow: {profile.ActiveFlow ?? "unknown"}",
                $"- last_intent: {profile.LastIntentType ?? "unknown"}",
                $"- last_semantic_intent: {profile.LastSemanticIntent ?? "unknown"}",
                $"- last_semantic_flow: {profile.LastSemanticFlowType ?? "unknown"}",
                $"- last_semantic_meaning: {profile.LastSemanticMeaning ?? "none"}"
            };

            if (profile.LastSemanticResult != null)
            {
                var last = profile.LastSemanticResult;
                lines.Add($"- last_semantic_brand: {last.Brand ?? "null"}");
                lines.Add($"- last_semantic_category: {last.Category ?? "null"}");
                lines.Add($"- last_semantic_target: {last.Target ?? "null"}");
                lines.Add($"- last_semantic_price_level: {last.PriceLevel}");
                lines.Add($"- last_semantic_installment: {last.Installment}");
                lines.Add($"- last_semantic_interest: {last.Interest}");
                lines.Add($"- last_semantic_policy_slot: {last.PolicySlot ?? "null"}");
                lines.Add($"- last_semantic_preference: {last.Preference}");
            }

            if (!string.IsNullOrWhiteSpace(profile.PreferredBrand))
                lines.Add($"- preferred_brand: {profile.PreferredBrand}");

            if (!string.IsNullOrWhiteSpace(profile.PreferredCategory))
                lines.Add($"- preferred_category: {profile.PreferredCategory}");

            if (!string.IsNullOrWhiteSpace(profile.Target))
                lines.Add($"- target: {profile.Target}");

            if (profile.TargetPrice.HasValue)
                lines.Add($"- target_price: {profile.TargetPrice.Value}");

            if (profile.PriceMin.HasValue)
                lines.Add($"- price_min: {profile.PriceMin.Value}");

            if (profile.PriceMax.HasValue)
                lines.Add($"- price_max: {profile.PriceMax.Value}");

            if (profile.LastMentionedProducts.Count > 0)
                lines.Add($"- last_mentioned_products: {string.Join(", ", profile.LastMentionedProducts)}");

            if (profile.LastComparedProducts.Count > 0)
                lines.Add($"- last_compared_products: {string.Join(", ", profile.LastComparedProducts)}");

            if (profile.LastRecommendedProducts.Count > 0)
                lines.Add($"- last_recommended_products: {string.Join(", ", profile.LastRecommendedProducts)}");

            lines.Add("- Reuse this context for short follow-up questions when the new utterance does not explicitly replace the old constraints.");

            return string.Join("\n", lines);
        }

        private static SemanticResult NormalizeResult(SemanticResult semantic, string rawMessage)
        {
            semantic.Intent = NormalizeEnum(
                semantic.Intent,
                "unknown",
                "product_lookup",
                "product_search",
                "recommend",
                "compare",
                "refine",
                "service_info",
                "policy_info",
                "greeting",
                "order_lookup",
                "out_of_scope",
                "unknown");
            semantic.FlowType = NormalizeEnum(
                semantic.FlowType,
                ChatFlowType.Unknown,
                ChatFlowType.ProductLookup,
                ChatFlowType.ProductSearch,
                ChatFlowType.Recommendation,
                ChatFlowType.Compare,
                ChatFlowType.Refinement,
                ChatFlowType.Greeting,
                ChatFlowType.OrderLookup,
                ChatFlowType.OutOfScope,
                ChatFlowType.Unknown);
            semantic.PriceFilterType = NormalizeEnum(semantic.PriceFilterType, "none", "none", "max_only", "min_only", "range", "around");
            semantic.PriceLevel = NormalizeEnum(semantic.PriceLevel, "khong_ro", "thap", "trung_binh", "cao", "khong_ro");
            semantic.Installment = NormalizeEnum(semantic.Installment, "khong_ro", "co", "khong", "khong_ro");
            semantic.Interest = NormalizeEnum(semantic.Interest, "khong_ro", "thap", "trung_binh", "cao", "khong_ro");
            semantic.Preference = semantic.Preference?.Trim() ?? string.Empty;
            semantic.PolicySlot = NormalizeNullableString(semantic.PolicySlot);
            semantic.NormalizedMeaning = NormalizeNormalizedMeaning(semantic, rawMessage);

            semantic.MentionedProducts ??= new List<string>();
            semantic.ExcludedBrands ??= new List<string>();
            semantic.ExcludedCategories ??= new List<string>();
            semantic.ExcludedProducts ??= new List<string>();
            semantic.RequestedStyles ??= new List<string>();

            semantic.IsFallback = false;
            return semantic;
        }

        private static SemanticResult BuildHeuristicFallback(string rawMessage)
        {
            var fallback = SemanticResult.Unknown(rawMessage);
            var normalized = NormalizeText(rawMessage);

            bool looksLikePolicyQuestion = ContainsAny(
                normalized,
                "trả góp cần gì",
                "tra gop can gi",
                "giấy tờ",
                "giay to",
                "hồ sơ",
                "ho so",
                "thủ tục",
                "thu tuc",
                "điều kiện",
                "dieu kien",
                "lãi suất",
                "lai suat",
                "chính sách",
                "chinh sach",
                "quy trình",
                "quy trinh");

            bool looksLikeServiceQuestion = ContainsAny(
                normalized,
                "shop có trả góp không",
                "shop co tra gop khong",
                "có trả góp không",
                "co tra gop khong",
                "bảo dưỡng",
                "bao duong",
                "sửa chữa",
                "sua chua",
                "dịch vụ",
                "dich vu",
                "bảo hành",
                "bao hanh");

            if (looksLikePolicyQuestion)
            {
                fallback.Intent = "policy_info";
                fallback.FlowType = ChatFlowType.Unknown;
            }
            else if (looksLikeServiceQuestion)
            {
                fallback.Intent = "service_info";
                fallback.FlowType = ChatFlowType.Unknown;
            }

            if (ContainsAny(normalized, "tra gop", "gop"))
            {
                fallback.Installment = ContainsAny(normalized, "khong muon tra gop", "khong can tra gop", "khong tra gop")
                    ? "khong"
                    : "co";

                if (fallback.Installment == "co" && ContainsAny(normalized, "lai nhe", "lai suat nhe", "tra gop nhe", "gop nhe"))
                {
                    fallback.Interest = "thap";
                }
            }

            if (ContainsAny(normalized, "gia", "re", "gia re", "gia mem", "mem", "binh dan", "nhe vi", "vua tui tien"))
            {
                fallback.PriceLevel = "thap";
            }
            else if (ContainsAny(normalized, "khong can xe xin", "khong can xe sang", "khong can xe cao cap", "khong lay xe dat"))
            {
                fallback.PriceLevel = "trung_binh";
                fallback.Preference = "khong uu tien xe cao cap";
            }

            if ((fallback.Installment == "co" || fallback.Installment == "khong") &&
                !string.Equals(fallback.Intent, "service_info", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fallback.Intent, "policy_info", StringComparison.OrdinalIgnoreCase))
            {
                fallback.Intent = "recommend";
                fallback.FlowType = ChatFlowType.Recommendation;
            }

            if (fallback.PriceLevel == "thap" && fallback.FlowType == ChatFlowType.Unknown)
            {
                fallback.Intent = "product_search";
                fallback.FlowType = ChatFlowType.ProductSearch;
            }

            fallback.NormalizedMeaning = NormalizeNormalizedMeaning(fallback, rawMessage);
            return fallback;
        }

        private static string NormalizeOrDefault(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }

        private static string? NormalizeNullableString(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private static string NormalizeNormalizedMeaning(SemanticResult semantic, string rawMessage)
        {
            var explicitMeaning = semantic.NormalizedMeaning?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(explicitMeaning) || explicitMeaning.Length > 120)
            {
                explicitMeaning = BuildNormalizedMeaningFromSlots(semantic);
            }

            explicitMeaning = string.Join(" ", explicitMeaning
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (string.IsNullOrWhiteSpace(explicitMeaning))
            {
                explicitMeaning = NormalizeOrDefault(rawMessage, "chua du thong tin");
            }

            if (explicitMeaning.Length > 120)
            {
                explicitMeaning = explicitMeaning[..120].Trim();
            }

            return explicitMeaning;
        }

        private static string BuildNormalizedMeaningFromSlots(SemanticResult semantic)
        {
            var constraints = new List<string>();

            if (!string.IsNullOrWhiteSpace(semantic.Brand))
                constraints.Add($"hang {semantic.Brand.Trim()}");

            if (!string.IsNullOrWhiteSpace(semantic.Category))
                constraints.Add($"loai {semantic.Category.Trim()}");

            if (semantic.PriceLevel == "thap")
                constraints.Add("gia re");
            else if (semantic.PriceLevel == "trung_binh")
                constraints.Add("tam trung");
            else if (semantic.PriceLevel == "cao")
                constraints.Add("cao cap");

            if (semantic.Installment == "co" && semantic.Interest == "thap")
                constraints.Add("tra gop lai thap");
            else if (semantic.Installment == "co")
                constraints.Add("tra gop");
            else if (semantic.Installment == "khong")
                constraints.Add("khong tra gop");

            if (!string.IsNullOrWhiteSpace(semantic.Preference))
                constraints.Add(semantic.Preference.Trim());

            if (!string.IsNullOrWhiteSpace(semantic.PolicySlot))
                constraints.Add(NormalizePolicySlotLabel(semantic.PolicySlot));

            if (constraints.Count == 0)
                return semantic.NormalizedMeaning?.Trim() ?? string.Empty;

            var prefix = semantic.Intent switch
            {
                "product_lookup" => "muon tra cuu xe",
                "product_search" => "muon tim xe",
                "recommend" => "muon duoc goi y xe",
                "compare" => "muon so sanh xe",
                "refine" => "muon loc lai xe",
                _ => "muon xe"
            };

            return $"{prefix} {string.Join(" va ", constraints)}".Trim();
        }

        private static string NormalizePolicySlotLabel(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "documents" => "ho so",
                "conditions" => "dieu kien",
                "process" => "quy trinh",
                "approval_time" => "thoi gian duyet",
                "interest" => "lai suat",
                "down_payment" => "tra truoc",
                "refund" => "hoan coc",
                "deposit" => "dat coc",
                "warranty_period" => "bao hanh",
                "warranty_exclusion" => "ngoai le bao hanh",
                "maintenance_schedule" => "lich bao duong",
                "fee" => "phi",
                "registration_time" => "thoi gian dang ky",
                _ => value.Trim()
            };
        }

        private static string NormalizeText(string value)
        {
            return value.Trim().ToLowerInvariant();
        }

        private static bool ContainsAny(string source, params string[] candidates)
        {
            return candidates.Any(candidate => source.Contains(candidate, StringComparison.Ordinal));
        }

        private static string NormalizeEnum(string? value, string fallback, params string[] allowedValues)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var normalized = value.Trim();
            return allowedValues.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))
                ? allowedValues.First(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase))
                : fallback;
        }

        private static object StringProperty(string description)
        {
            return new
            {
                type = "string",
                description
            };
        }

        private static object EnumStringProperty(string description, params string[] values)
        {
            return new
            {
                type = "string",
                description,
                @enum = values
            };
        }

        private static object NullableStringProperty()
        {
            return new
            {
                type = new[] { "string", "null" }
            };
        }

        private static object NullableNumberProperty()
        {
            return new
            {
                type = new[] { "number", "null" }
            };
        }

        private static object NullableIntegerProperty()
        {
            return new
            {
                type = new[] { "integer", "null" }
            };
        }

        private static object BoolProperty()
        {
            return new
            {
                type = "boolean"
            };
        }

        private static object StringArrayProperty()
        {
            return new
            {
                type = "array",
                items = new
                {
                    type = "string"
                }
            };
        }

        private sealed class ChatCompletionResponse
        {
            public List<Choice> Choices { get; set; } = new();
        }

        private sealed class Choice
        {
            public Message Message { get; set; } = new();
        }

        private sealed class Message
        {
            public string? Content { get; set; }
        }
    }
}
