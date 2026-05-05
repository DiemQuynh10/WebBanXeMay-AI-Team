using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services
{
    internal static class RagContextPostProcessor
    {
        private static readonly string[] Brands = { "honda", "yamaha", "suzuki", "piaggio", "sym" };

        private static readonly Dictionary<string, string[]> DomainKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["installment"] = new[] { "tra gop", "lai suat", "tra truoc", "vay", "tin dung", "ngan hang", "hd saison", "fe credit", "home credit", "tat toan", "ho so vay" },
            ["warranty"] = new[] { "bao hanh", "chinh hang", "dong co", "khung xe", "phu tung", "khong bao hanh", "trung tam uy quyen" },
            ["maintenance"] = new[] { "bao duong", "thay nhot", "bugi", "loc gio", "loc nhot", "day curoa", "dinh ky", "goi bao duong" },
            ["paperwork"] = new[] { "giay to", "bien so", "ca vet", "dang ky", "truoc ba", "cu tru", "ct07", "vneid", "bao bien" },
            ["delivery"] = new[] { "giao hang", "van chuyen", "tan noi", "noi thanh", "ngoai thanh", "tinh lan can" },
            ["insurance"] = new[] { "bao hiem", "tnds", "tai nan", "mat cap", "boi thuong", "vat chat" },
            ["promotion"] = new[] { "khuyen mai", "uu dai", "giam", "tang", "sinh vien", "phu kien" },
            ["deposit"] = new[] { "dat coc", "giu xe", "hoan coc", "khong duoc duyet" },
            ["tradein"] = new[] { "thu cu", "doi moi", "trade-in", "len doi", "dinh gia", "tro gia" },
            ["payment"] = new[] { "thanh toan", "chuyen khoan", "quet the", "visa", "mastercard", "phi giao dich", "mua xe online" },
            ["testride"] = new[] { "lai thu", "test ride", "bang lai", "a1", "a2", "sa hinh" },
            ["technology"] = new[] { "abs", "cbs", "smartkey", "chia khoa", "fi", "phun xang" }
        };

        private static readonly Dictionary<string, string[]> SlotKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = new[] { "documents", "ho so", "giay to", "cmnd", "cccd", "ho khau", "kt3", "hop dong lao dong", "sao ke" },
            ["conditions"] = new[] { "conditions", "dieu kien", "do tuoi", "thu nhap", "cic", "no xau", "bao lanh" },
            ["process"] = new[] { "process", "quy trinh", "buoc", "ky hop dong", "tham dinh" },
            ["approval_time"] = new[] { "approval_time", "duyet", "15", "30 phut", "nhan xe" },
            ["interest"] = new[] { "interest", "lai suat", "0%", "0.5", "1.2", "1.5" },
            ["down_payment"] = new[] { "down_payment", "tra truoc", "down payment", "0 dong", "20%", "50%" },
            ["loan_term"] = new[] { "loan_term", "thoi gian vay", "12", "60 thang" },
            ["monthly_payment"] = new[] { "monthly_payment", "hang thang", "moi thang", "1.5 trieu" },
            ["early_settlement"] = new[] { "early_settlement", "tat toan", "phi phat", "du no goc" },
            ["refund"] = new[] { "refund", "hoan", "hoan coc", "hoan lai", "doi tra" },
            ["deposit"] = new[] { "deposit", "dat coc", "giu xe", "hoan coc", "khong duoc duyet" },
            ["warranty_period"] = new[] { "warranty_period", "bao hanh", "nam", "km", "khong gioi han" },
            ["warranty_exclusion"] = new[] { "warranty_exclusion", "khong bao hanh", "hao mon", "lop xe", "ma phanh", "bugi", "bong den", "dau nhot" },
            ["service_package"] = new[] { "service_package", "goi", "500.000", "800.000", "1.200.000" },
            ["maintenance_schedule"] = new[] { "maintenance_schedule", "500km", "3,000km", "6,000km", "12,000km", "dinh ky", "ro-dai" },
            ["fee"] = new[] { "fee", "phi", "66.000", "100.000", "200.000", "500.000", "1.500.000", "2-4 trieu" },
            ["registration_time"] = new[] { "registration_time", "bam bien", "ca vet", "1-3 ngay", "7-10 ngay" }
        };

        public static string Filter(
            string? ragContext,
            string normalizedMessage,
            string semanticQuery,
            ParsedIntent? intent,
            CustomerPreferenceProfile? profile,
            int maxChunks = 5)
        {
            if (!HasUsableContext(ragContext))
                return string.Empty;

            var question = JoinParts(normalizedMessage, semanticQuery, profile?.LastSemanticMeaning);
            var requestedDomains = InferDomains(question, intent);
            var requestedSlots = InferSlots(question);
            if (IsPurchaseProcedureQuestion(question))
            {
                requestedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "paperwork"
    };

                requestedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "documents",
        "process",
        "registration_time"
    };
            }
            var preferredSlots = ExpandPreferredSlots(requestedSlots);
            var requestedBrands = InferBrands(question, intent, profile);
            var queryTokens = Tokenize(question);

            var facts = ExtractFacts(ragContext!)
                .Select((fact, index) => fact with
                {
                    Index = index,
                    Score = ScoreFact(fact, requestedDomains, requestedSlots, requestedBrands, queryTokens)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Index)
                .ToList();

            if (preferredSlots.Count > 0)
            {
                var preferredFacts = facts.Where(x => preferredSlots.Contains(x.Slot)).ToList();
                if (preferredFacts.Count > 0)
                    facts = preferredFacts;
            }

            if (facts.Count == 0)
                return ragContext!.Trim();

            var hasRelevantFact = facts.Any(x => x.Score > 0);
            var selected = facts
                .Where(x => !hasRelevantFact || x.Score > 0)
                .Take(Math.Max(1, maxChunks))
                .OrderBy(x => x.Index)
                .ToList();

            return FormatFactsForPrompt(selected.Count > 0 ? selected : facts.Take(1));
        }

        public static string BuildGroundedReply(string? ragContext)
        {
            if (!HasUsableContext(ragContext))
            {
                return "M\u00ecnh ch\u01b0a c\u00f3 \u0111\u1ee7 d\u1eef li\u1ec7u ch\u00ednh x\u00e1c v\u1ec1 ph\u1ea7n n\u00e0y. B\u1ea1n n\u00f3i r\u00f5 h\u01a1n m\u1ed9t ch\u00fat \u0111\u1ec3 m\u00ecnh ki\u1ec3m tra \u0111\u00fang ch\u00ednh s\u00e1ch nh\u00e9.";
            }

            var values = ExtractPromptFactValues(ragContext!)
                .Concat(ExtractFacts(ragContext!).Select(x => x.Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (values.Count == 0)
                return TrimToLimit(ragContext!, 700);

            return TrimToLimit($"D\u1ea1, theo th\u00f4ng tin b\u00ean m\u00ecnh: {string.Join(" ", values)}", 700);
        }

        public static bool HasUsableContext(string? ragContext)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return false;

            var normalized = Normalize(ragContext);
            return !normalized.Contains("khong tim thay du lieu", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeNoDataReply(string? reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var normalized = Normalize(reply);
            return normalized.Contains("chua co du du lieu", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("khong co du lieu", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("khong tim thay du lieu", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("chua co thong tin", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("tuy tung dong xe", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<KnowledgeFact> ExtractFacts(string context)
        {
            var currentDomain = "general";
            string? currentBrand = null;

            foreach (var rawLine in context.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line == "---")
                    continue;

                var normalized = Normalize(line);

                var headingDomain = InferDomainFromText(normalized);
                if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("[", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(headingDomain))
                        currentDomain = headingDomain;
                    continue;
                }

                var brand = InferBrandFromText(normalized) ?? currentBrand;
                if (brand != null && Regex.IsMatch(line, @"^[A-Za-z]+\s*:", RegexOptions.IgnoreCase))
                    currentBrand = brand;

                var domain = headingDomain ?? currentDomain;
                var slot = InferSlotFromText(normalized, domain);
                var value = CleanFactValue(line);

                if (value.Length < 6)
                    continue;

                yield return new KnowledgeFact(domain, slot, brand, value);
            }
        }

        private static int ScoreFact(
            KnowledgeFact fact,
            IReadOnlySet<string> requestedDomains,
            IReadOnlySet<string> requestedSlots,
            IReadOnlySet<string> requestedBrands,
            IReadOnlySet<string> queryTokens)
        {
            var text = Normalize($"{fact.Domain} {fact.Slot} {fact.Brand} {fact.Value}");
            var score = 0;
            bool questionLooksLikePurchaseProcedure =
    requestedDomains.Contains("paperwork") &&
    requestedSlots.Contains("process");

            if (questionLooksLikePurchaseProcedure &&
                fact.Domain.Equals("testride", StringComparison.OrdinalIgnoreCase))
            {
                score -= 50;
            }

            if (questionLooksLikePurchaseProcedure &&
                Normalize(fact.Value).Contains("lai thu"))
            {
                score -= 50;
            }

            if (questionLooksLikePurchaseProcedure &&
                Normalize(fact.Value).Contains("ky bien ban cam ket an toan"))
            {
                score -= 50;
            }
            if (requestedDomains.Count > 0 && requestedDomains.Contains(fact.Domain))
                score += 20;

            if (requestedSlots.Count > 0 && requestedSlots.Contains(fact.Slot))
                score += 18;

            if (requestedBrands.Count > 0 && fact.Brand != null && requestedBrands.Contains(fact.Brand))
                score += 14;

            if (requestedBrands.Count > 0 && fact.Brand == null)
                score += 2;

            score += queryTokens.Count(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

            if (Regex.IsMatch(fact.Value, @"\d"))
                score += 3;

            return score;
        }

        private static string FormatFactsForPrompt(IEnumerable<KnowledgeFact> facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Structured RAG facts. Use these facts first; do not answer generically when a value is present.");

            foreach (var fact in facts)
            {
                sb.Append("- domain: ").Append(fact.Domain)
                    .Append("; slot: ").Append(fact.Slot);

                if (!string.IsNullOrWhiteSpace(fact.Brand))
                    sb.Append("; brand: ").Append(fact.Brand);

                sb.Append("; value: ").AppendLine(fact.Value);
            }

            return sb.ToString().Trim();
        }

        private static IEnumerable<string> ExtractPromptFactValues(string context)
        {
            foreach (Match match in Regex.Matches(context, @"value:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var value = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return EnsureSentence(value);
            }
        }

        private static IReadOnlySet<string> InferDomains(string question, ParsedIntent? intent)
        {
            var normalized = Normalize(JoinParts(question, intent?.IntentType, intent?.LookupField, intent?.ComparisonFeature));
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in DomainKeywords)
            {
                if (kvp.Value.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    result.Add(kvp.Key);
            }

            if (result.Count == 0 && IsServiceOrPolicyIntent(intent?.IntentType))
            {
                result.Add("installment");
                result.Add("warranty");
                result.Add("maintenance");
                result.Add("paperwork");
            }

            return result;
        }

        private static IReadOnlySet<string> InferSlots(string question)
        {
            var normalized = Normalize(question);
            return SlotKeywords
                .Where(kvp => kvp.Value.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlySet<string> InferBrands(string question, ParsedIntent? intent, CustomerPreferenceProfile? profile)
        {
            var normalized = Normalize(JoinParts(question, intent?.Brand, profile?.PreferredBrand));
            return Brands
                .Where(brand => normalized.Contains(brand, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlySet<string> ExpandPreferredSlots(IReadOnlySet<string> requestedSlots)
        {
            if (requestedSlots.Count == 0)
                return requestedSlots;

            var preferredSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (requestedSlots.Contains("documents") ||
                requestedSlots.Contains("conditions") ||
                requestedSlots.Contains("process") ||
                requestedSlots.Contains("approval_time"))
            {
                preferredSlots.UnionWith(new[] { "documents", "conditions", "process", "approval_time" });
            }

            if (requestedSlots.Contains("interest") ||
                requestedSlots.Contains("down_payment") ||
                requestedSlots.Contains("loan_term") ||
                requestedSlots.Contains("monthly_payment") ||
                requestedSlots.Contains("early_settlement") ||
                requestedSlots.Contains("refund") ||
                requestedSlots.Contains("deposit") ||
                requestedSlots.Contains("fee"))
            {
                preferredSlots.UnionWith(new[] { "interest", "down_payment", "loan_term", "monthly_payment", "early_settlement", "refund", "deposit", "fee" });
            }

            if (requestedSlots.Contains("warranty_period") || requestedSlots.Contains("warranty_exclusion"))
            {
                preferredSlots.UnionWith(new[] { "warranty_period", "warranty_exclusion" });
            }

            if (requestedSlots.Contains("maintenance_schedule") || requestedSlots.Contains("service_package"))
            {
                preferredSlots.UnionWith(new[] { "maintenance_schedule", "service_package", "fee" });
            }

            if (requestedSlots.Contains("registration_time"))
            {
                preferredSlots.Add("registration_time");
            }

            return preferredSlots.Count > 0
                ? preferredSlots
                : requestedSlots;
        }

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string? InferDomainFromText(string normalizedText)
        {
            foreach (var kvp in DomainKeywords)
            {
                if (kvp.Value.Any(k => normalizedText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return kvp.Key;
            }

            return null;
        }

        private static string InferSlotFromText(string normalizedText, string domain)
        {
            if (ContainsAny(normalizedText, SlotKeywords["refund"]))
                return "refund";

            if (ContainsAny(normalizedText, SlotKeywords["deposit"]))
                return "deposit";

            if (ContainsAny(normalizedText, SlotKeywords["down_payment"]))
                return "down_payment";

            if (ContainsAny(normalizedText, SlotKeywords["interest"]))
                return "interest";

            if (ContainsAny(normalizedText, SlotKeywords["loan_term"]))
                return "loan_term";

            if (ContainsAny(normalizedText, SlotKeywords["monthly_payment"]))
                return "monthly_payment";

            if (ContainsAny(normalizedText, SlotKeywords["early_settlement"]))
                return "early_settlement";

            if (ContainsAny(normalizedText, SlotKeywords["approval_time"]))
                return "approval_time";

            if (ContainsAny(normalizedText, SlotKeywords["conditions"]))
                return "conditions";

            if (ContainsAny(normalizedText, SlotKeywords["process"]))
                return "process";

            if (ContainsAny(normalizedText, SlotKeywords["documents"]))
                return "documents";

            if (ContainsAny(normalizedText, SlotKeywords["warranty_period"]))
                return "warranty_period";

            if (ContainsAny(normalizedText, SlotKeywords["warranty_exclusion"]))
                return "warranty_exclusion";

            if (ContainsAny(normalizedText, SlotKeywords["service_package"]))
                return "service_package";

            if (ContainsAny(normalizedText, SlotKeywords["maintenance_schedule"]))
                return "maintenance_schedule";

            if (ContainsAny(normalizedText, SlotKeywords["fee"]))
                return "fee";

            if (ContainsAny(normalizedText, SlotKeywords["registration_time"]))
                return "registration_time";

            return domain switch
            {
                "installment" => "policy",
                "warranty" => "policy",
                "maintenance" => "policy",
                "paperwork" => "policy",
                _ => "general"
            };
        }

        private static string? InferBrandFromText(string normalizedText)
        {
            return Brands.FirstOrDefault(brand => normalizedText.Contains(brand, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlySet<string> Tokenize(string text)
        {
            var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cho", "toi", "minh", "ban", "hoi", "ve", "la", "co", "khong", "nhu", "the", "nao", "bao", "nhieu", "duoc", "can", "shop", "cua", "hang"
            };

            return Regex.Matches(Normalize(text), @"[a-z0-9]{3,}")
                .Select(m => m.Value)
                .Where(x => !stopwords.Contains(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string CleanFactValue(string line)
        {
            var value = Regex.Replace(line.Trim(), @"^\d+\.\s*", string.Empty);
            value = Regex.Replace(value, @"^[-+*]\s*", string.Empty);
            value = Regex.Replace(value, @"^\s*[A-Za-z]+\s*:\s*", m => m.Value.TrimEnd() + " ");
            return EnsureSentence(value);
        }

        private static string EnsureSentence(string value)
        {
            var cleaned = value.Trim();
            if (cleaned.Length == 0)
                return cleaned;

            return cleaned.EndsWith('.') || cleaned.EndsWith('?') || cleaned.EndsWith('!')
                ? cleaned
                : cleaned + ".";
        }

        private static bool IsServiceOrPolicyIntent(string? intentType)
        {
            return string.Equals(intentType, "service_info", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intentType, "policy_info", StringComparison.OrdinalIgnoreCase);
        }

        private static string JoinParts(params string?[] parts)
        {
            return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        }

        private static string TrimToLimit(string text, int limit)
        {
            var cleaned = text.Trim();
            return cleaned.Length <= limit ? cleaned : cleaned[..limit].Trim() + "...";
        }

        private static string Normalize(string text)
        {
            var formD = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);

            foreach (var ch in formD)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('đ', 'd')
                .Replace('Đ', 'D')
                .ToLowerInvariant();
        }
        private static bool IsPurchaseProcedureQuestion(string question)
        {
            var text = Normalize(question);

            bool asksProcedure =
                text.Contains("thu tuc") ||
                text.Contains("quy trinh") ||
                text.Contains("nhu nao") ||
                text.Contains("the nao");

            bool purchaseContext =
     text.Contains("mua xe") ||
     text.Contains("giay to") ||
     text.Contains("giay") ||
     text.Contains("to roi") ||
     text.Contains("ho so") ||
     text.Contains("dang ky xe") ||
     text.Contains("bien so") ||
     text.Contains("cccd") ||
     text.Contains("cmnd");

            bool explicitOtherPolicy =
                text.Contains("lai thu") ||
                text.Contains("test ride") ||
                text.Contains("bao hanh") ||
                text.Contains("doi tra") ||
                text.Contains("hoan tien") ||
                text.Contains("giao hang") ||
                text.Contains("dat coc") ||
                text.Contains("bao duong");

            return asksProcedure && purchaseContext && !explicitOtherPolicy;
        }
        private sealed record KnowledgeFact(string Domain, string Slot, string? Brand, string Value)
        {
            public int Index { get; init; }
            public int Score { get; init; }
        }
    }
}
