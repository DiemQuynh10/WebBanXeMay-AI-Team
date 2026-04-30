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
            ["return_policy"] = new[] { "doi tra", "tra xe", "hoan tien", "khau hao", "hoa don vat", "lan banh", "nguyen tem" },
            ["delivery"] = new[] { "giao hang", "van chuyen", "tan noi", "noi thanh", "ngoai thanh", "tinh lan can" },
            ["insurance"] = new[] { "bao hiem", "tnds", "tai nan", "mat cap", "boi thuong", "vat chat" },
            ["promotion"] = new[] { "khuyen mai", "uu dai", "giam", "tang", "sinh vien", "phu kien" },
            ["pricing"] = new[] { "gia lan banh", "bao giay", "gia niem yet", "phi cap bien", "le phi truoc ba" },
            ["deposit"] = new[] { "dat coc", "giu xe", "hoan coc", "khong duoc duyet" },
            ["tradein"] = new[] { "thu cu", "doi moi", "trade-in", "len doi", "dinh gia", "tro gia" },
            ["payment"] = new[] { "thanh toan", "chuyen khoan", "quet the", "visa", "mastercard", "phi giao dich", "mua xe online" },
            ["testride"] = new[] { "lai thu", "test ride", "bang lai", "a1", "a2", "sa hinh" },
            ["technology"] = new[] { "abs", "cbs", "smartkey", "chia khoa", "fi", "phun xang" },
            ["connected_app"] = new[] { "my honda", "y-connect", "app", "ung dung", "bluetooth", "bao duong dien tu" },
            ["rescue"] = new[] { "cuu ho", "thung lop", "chet may", "mat chia khoa", "het xang", "khẩn cấp", "khan cap" },
            ["faq"] = new[] { "hao xang", "ton xang", "bao duong o que", "bao hanh toan quoc", "co san", "giao ngay" },
            ["fengshui"] = new[] { "phong thuy", "menh", "mau xe", "hop mau", "kim", "moc", "thuy", "hoa", "tho" }
        };

        private static readonly Dictionary<string, string[]> SlotKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = new[] { "documents", "ho so", "giay to", "cmnd", "cccd", "ho khau", "kt3", "hop dong lao dong", "sao ke" },
            ["conditions"] = new[] { "conditions", "dieu kien", "do tuoi", "thu nhap", "cic", "no xau", "bao lanh" },
            ["process"] = new[] { "process", "quy trinh", "buoc", "ky hop dong", "tham dinh" },
            ["approval_time"] = new[] { "approval_time", "duyet", "15", "30 phut", "nhan xe" },
            ["interest"] = new[] { "interest", "lai suat", "0%", "0.5", "1.2", "1.5" },
            ["zero_interest"] = new[] { "zero_interest", "0%", "0 %", "khong lai", "lai suat 0" },
            ["down_payment"] = new[] { "down_payment", "tra truoc", "down payment", "0 dong", "20%", "50%" },
            ["loan_term"] = new[] { "loan_term", "ky han", "ki han", "thoi gian vay", "may thang", "bao lau", "12", "60 thang" },
            ["monthly_payment"] = new[] { "monthly_payment", "hang thang", "moi thang", "1.5 trieu" },
            ["early_settlement"] = new[] { "early_settlement", "tat toan", "phi phat", "du no goc" },
            ["refund"] = new[] { "refund", "hoan", "hoan coc", "hoan lai", "doi tra" },
            ["deposit"] = new[] { "deposit", "dat coc", "giu xe", "hoan coc", "khong duoc duyet" },
            ["deposit_amount"] = new[] { "deposit_amount", "muc coc", "tien coc", "1.000.000", "3.000.000" },
            ["listed_price"] = new[] { "listed_price", "gia niem yet", "gia xe", "da co vat" },
            ["onroad_price"] = new[] { "onroad_price", "gia lan banh", "bao giay", "ra bien", "chay ra duong" },
            ["plate_fee"] = new[] { "plate_fee", "phi cap bien", "bien so", "2-4 trieu", "ho khau" },
            ["registration_tax"] = new[] { "registration_tax", "le phi truoc ba", "truoc ba", "10-15%", "5%", "2%" },
            ["plate_identity"] = new[] { "plate_identity", "bien so dinh danh", "giu lai bien", "di theo nguoi" },
            ["warranty_period"] = new[] { "warranty_period", "bao hanh", "nam", "km", "khong gioi han" },
            ["warranty_engine"] = new[] { "warranty_engine", "dong co" },
            ["warranty_frame"] = new[] { "warranty_frame", "khung xe", "nứt gãy", "nut gay", "moi han" },
            ["warranty_parts"] = new[] { "warranty_parts", "phu tung", "linh kien" },
            ["warranty_conditions"] = new[] { "warranty_conditions", "dieu kien bao hanh", "trung tam uy quyen", "phieu giay", "tu y sua chua" },
            ["warranty_exclusion"] = new[] { "warranty_exclusion", "khong bao hanh", "hao mon", "lop xe", "ma phanh", "bugi", "bong den", "dau nhot" },
            ["service_package"] = new[] { "service_package", "goi", "500.000", "800.000", "1.200.000" },
            ["basic_service_package"] = new[] { "basic_service_package", "goi co ban", "500.000", "kiem tra tong the", "thay nhot" },
            ["advanced_service_package"] = new[] { "advanced_service_package", "goi nang cao", "800.000", "bugi", "day curoa", "ac quy" },
            ["premium_service_package"] = new[] { "premium_service_package", "goi cao cap", "1.200.000", "loc nhien lieu", "he thong treo" },
            ["maintenance_schedule"] = new[] { "maintenance_schedule", "500km", "3,000km", "6,000km", "12,000km", "dinh ky", "ro-dai" },
            ["break_in_service"] = new[] { "break_in_service", "ro-dai", "500km dau", "thay nhot lan dau" },
            ["fee"] = new[] { "fee", "phi", "mien phi", "66.000", "100.000", "200.000", "500.000", "1.500.000", "2-4 trieu" },
            ["registration_time"] = new[] { "registration_time", "bam bien", "ca vet", "1-3 ngay", "7-10 ngay" },
            ["required_vehicle_documents"] = new[] { "required_vehicle_documents", "ca vet", "giay dang ky", "kiem dinh", "tnds bat buoc" },
            ["plate_service"] = new[] { "plate_service", "bao bien", "lam giay to tron goi", "ct07", "vneid" },
            ["registration_process"] = new[] { "registration_process", "tu dang ky", "to khai", "nop le phi", "nhan bien" },
            ["return_boundary"] = new[] { "return_boundary", "chua xuat hoa don", "da xuat hoa don", "lan banh", "xe cu" },
            ["return_conditions"] = new[] { "return_conditions", "nguyen tem", "niem phong", "phu kien", "qua tang" },
            ["return_process"] = new[] { "return_process", "mang xe", "ktv kiem tra", "xac nhan", "hoan tien", "doi xe" },
            ["depreciation"] = new[] { "depreciation", "khau hao", "10-20%" },
            ["delivery_area"] = new[] { "delivery_area", "noi thanh", "ngoai thanh", "khu vuc", "tinh lan can" },
            ["delivery_fee"] = new[] { "delivery_fee", "phi giao", "mien phi", "100.000", "200.000", "tinh theo km" },
            ["delivery_time"] = new[] { "delivery_time", "1-2 gio", "2-4 gio", "1-2 ngay", "2-3 ngay" },
            ["delivery_risk"] = new[] { "delivery_risk", "rui ro van chuyen", "chiu 100%", "ky nhan" },
            ["delivery_conditions"] = new[] { "delivery_conditions", "thanh toan du", "kiem dinh", "co mat nhan xe" },
            ["delivery_inspection"] = new[] { "delivery_inspection", "no may", "kiem tra ngoai quan", "truoc khi nhan" },
            ["compulsory_insurance"] = new[] { "compulsory_insurance", "tnds", "bat buoc", "66.000", "nguoi bi tong" },
            ["voluntary_insurance"] = new[] { "voluntary_insurance", "tu nguyen", "mat cap", "toan dien", "1-1.5%" },
            ["theft_insurance"] = new[] { "theft_insurance", "mat cap", "chia goc", "ho so cong an", "70-80%" },
            ["accident_insurance"] = new[] { "accident_insurance", "tai nan", "20.000" },
            ["current_promotion"] = new[] { "current_promotion", "khuyen mai hien tai", "thang 4", "giam 5%", "qua 500.000" },
            ["student_promotion"] = new[] { "student_promotion", "sinh vien", "the sv", "giay bao trung tuyen", "balo" },
            ["promotion_conditions"] = new[] { "promotion_conditions", "dieu kien ap dung", "qua hien vat", "khong quy doi" },
            ["tradein_valuation"] = new[] { "tradein_valuation", "dinh gia", "15 phut", "kiem tra xe cu" },
            ["tradein_documents"] = new[] { "tradein_documents", "chinh chu", "hop dong mua ban", "uy quyen", "so khung", "so may" },
            ["tradein_voucher"] = new[] { "tradein_voucher", "tro gia", "voucher", "1.000.000", "2.000.000" },
            ["tradein_payment"] = new[] { "tradein_payment", "chenh lech", "tra gop phan chenh lech" },
            ["payment_methods"] = new[] { "payment_methods", "tien mat", "chuyen khoan", "atm", "visa", "mastercard", "jcb" },
            ["credit_card_fee"] = new[] { "credit_card_fee", "phi quet the", "the tin dung", "1.5%", "2.5%" },
            ["online_purchase"] = new[] { "online_purchase", "mua xe online", "video call", "so khung", "so may", "thanh toan phan con lai" },
            ["testride_models"] = new[] { "testride_models", "dong xe co san lai thu", "vario", "exciter", "winner" },
            ["testride_conditions"] = new[] { "testride_conditions", "bang lai", "a1", "a2", "cmnd", "cccd" },
            ["testride_process"] = new[] { "testride_process", "dat lich", "xuat trinh", "ky bien ban", "sa hinh" },
            ["smart_app"] = new[] { "smart_app", "my honda", "y-connect", "bao hanh dien tu", "nhac lich bao duong", "bluetooth" },
            ["rescue_cases"] = new[] { "rescue_cases", "thung lop", "chet may", "mat chia khoa", "het xang" },
            ["rescue_fee"] = new[] { "rescue_fee", "phi cuu ho", "mien phi", "10km", "bao gia truoc" },
            ["fuel_consumption_faq"] = new[] { "fuel_consumption_faq", "hao xang", "ton xang", "lit/100km", "fi" },
            ["nationwide_warranty"] = new[] { "nationwide_warranty", "bao hanh toan quoc", "ve que", "head", "yamaha town" },
            ["plate_fee_reason"] = new[] { "plate_fee_reason", "phi bien so", "moi noi mot gia", "phan vung", "ha noi", "tp.hcm" },
            ["stock_availability"] = new[] { "stock_availability", "co san", "giao ngay", "check kho", "mau dac biet" },
            ["fengshui_color"] = new[] { "fengshui_color", "phong thuy", "menh", "mau xe", "hop mau", "kim", "moc", "thuy", "hoa", "tho" }
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
            var requestedSlots = InferSlots(question, intent);
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

        private static IReadOnlySet<string> InferSlots(string question, ParsedIntent? intent)
        {
            var normalized = Normalize(question);
            var slots = SlotKeywords
                .Where(kvp => kvp.Value.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(intent?.PolicySlot))
                slots.Add(intent.PolicySlot);

            return slots;
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
                requestedSlots.Contains("zero_interest") ||
                requestedSlots.Contains("refund") ||
                requestedSlots.Contains("deposit") ||
                requestedSlots.Contains("deposit_amount") ||
                requestedSlots.Contains("fee"))
            {
                preferredSlots.UnionWith(new[] { "interest", "zero_interest", "down_payment", "loan_term", "monthly_payment", "early_settlement", "refund", "deposit", "deposit_amount", "fee" });
            }

            if (requestedSlots.Contains("warranty_period") ||
                requestedSlots.Contains("warranty_engine") ||
                requestedSlots.Contains("warranty_frame") ||
                requestedSlots.Contains("warranty_parts") ||
                requestedSlots.Contains("warranty_conditions") ||
                requestedSlots.Contains("warranty_exclusion") ||
                requestedSlots.Contains("nationwide_warranty"))
            {
                preferredSlots.UnionWith(new[] { "warranty_period", "warranty_engine", "warranty_frame", "warranty_parts", "warranty_conditions", "warranty_exclusion", "nationwide_warranty" });
            }

            if (requestedSlots.Contains("maintenance_schedule") ||
                requestedSlots.Contains("service_package") ||
                requestedSlots.Contains("basic_service_package") ||
                requestedSlots.Contains("advanced_service_package") ||
                requestedSlots.Contains("premium_service_package") ||
                requestedSlots.Contains("break_in_service"))
            {
                preferredSlots.UnionWith(new[] { "maintenance_schedule", "service_package", "basic_service_package", "advanced_service_package", "premium_service_package", "break_in_service", "fee" });
            }

            if (requestedSlots.Contains("registration_time"))
            {
                preferredSlots.Add("registration_time");
            }

            if (requestedSlots.Contains("delivery_area") ||
                requestedSlots.Contains("delivery_fee") ||
                requestedSlots.Contains("delivery_time") ||
                requestedSlots.Contains("delivery_risk") ||
                requestedSlots.Contains("delivery_conditions") ||
                requestedSlots.Contains("delivery_inspection"))
            {
                preferredSlots.UnionWith(new[] { "delivery_area", "delivery_fee", "delivery_time", "delivery_risk", "delivery_conditions", "delivery_inspection", "fee" });
            }

            if (requestedSlots.Contains("fengshui_color"))
            {
                preferredSlots.Add("fengshui_color");
            }

            if (requestedSlots.Contains("listed_price") ||
                requestedSlots.Contains("onroad_price") ||
                requestedSlots.Contains("plate_fee") ||
                requestedSlots.Contains("registration_tax") ||
                requestedSlots.Contains("plate_identity") ||
                requestedSlots.Contains("plate_fee_reason"))
                preferredSlots.UnionWith(new[] { "listed_price", "onroad_price", "plate_fee", "registration_tax", "plate_identity", "plate_fee_reason" });

            if (requestedSlots.Contains("required_vehicle_documents") ||
                requestedSlots.Contains("plate_service") ||
                requestedSlots.Contains("registration_process"))
                preferredSlots.UnionWith(new[] { "required_vehicle_documents", "plate_service", "registration_process", "registration_time", "fee" });

            if (requestedSlots.Contains("return_boundary") || requestedSlots.Contains("return_conditions") || requestedSlots.Contains("return_process") || requestedSlots.Contains("depreciation"))
                preferredSlots.UnionWith(new[] { "return_boundary", "return_conditions", "return_process", "depreciation", "refund" });

            if (requestedSlots.Contains("payment_methods") || requestedSlots.Contains("credit_card_fee") || requestedSlots.Contains("online_purchase"))
                preferredSlots.UnionWith(new[] { "payment_methods", "credit_card_fee", "online_purchase", "deposit_amount" });

            if (requestedSlots.Contains("tradein_valuation") || requestedSlots.Contains("tradein_documents") || requestedSlots.Contains("tradein_voucher") || requestedSlots.Contains("tradein_payment"))
                preferredSlots.UnionWith(new[] { "tradein_valuation", "tradein_documents", "tradein_voucher", "tradein_payment" });

            if (requestedSlots.Contains("testride_models") || requestedSlots.Contains("testride_conditions") || requestedSlots.Contains("testride_process"))
                preferredSlots.UnionWith(new[] { "testride_models", "testride_conditions", "testride_process" });

            if (requestedSlots.Contains("rescue_cases") || requestedSlots.Contains("rescue_fee"))
                preferredSlots.UnionWith(new[] { "rescue_cases", "rescue_fee" });

            if (requestedSlots.Contains("compulsory_insurance") ||
                requestedSlots.Contains("voluntary_insurance") ||
                requestedSlots.Contains("theft_insurance") ||
                requestedSlots.Contains("accident_insurance"))
                preferredSlots.UnionWith(new[] { "compulsory_insurance", "voluntary_insurance", "theft_insurance", "accident_insurance", "fee" });

            if (requestedSlots.Contains("current_promotion") ||
                requestedSlots.Contains("student_promotion") ||
                requestedSlots.Contains("promotion_conditions"))
                preferredSlots.UnionWith(new[] { "current_promotion", "student_promotion", "promotion_conditions" });

            if (requestedSlots.Contains("smart_app"))
                preferredSlots.Add("smart_app");

            if (requestedSlots.Contains("fuel_consumption_faq") || requestedSlots.Contains("stock_availability"))
                preferredSlots.UnionWith(new[] { "fuel_consumption_faq", "stock_availability" });

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

            if (ContainsAny(normalizedText, SlotKeywords["plate_fee"]))
                return "plate_fee";

            if (ContainsAny(normalizedText, SlotKeywords["delivery_fee"]))
                return "delivery_fee";

            if (ContainsAny(normalizedText, SlotKeywords["credit_card_fee"]))
                return "credit_card_fee";

            if (ContainsAny(normalizedText, SlotKeywords["rescue_fee"]))
                return "rescue_fee";

            if (ContainsAny(normalizedText, SlotKeywords["fee"]))
                return "fee";

            if (ContainsAny(normalizedText, SlotKeywords["registration_time"]))
                return "registration_time";

            if (ContainsAny(normalizedText, SlotKeywords["delivery_area"]))
                return "delivery_area";

            if (ContainsAny(normalizedText, SlotKeywords["fengshui_color"]))
                return "fengshui_color";

            foreach (var kvp in SlotKeywords)
            {
                if (ContainsAny(normalizedText, kvp.Value))
                    return kvp.Key;
            }

            return domain switch
            {
                "installment" => "policy",
                "warranty" => "policy",
                "maintenance" => "policy",
                "paperwork" => "policy",
                "return_policy" => "policy",
                "delivery" => "policy",
                "insurance" => "policy",
                "promotion" => "policy",
                "pricing" => "policy",
                "deposit" => "policy",
                "tradein" => "policy",
                "payment" => "policy",
                "testride" => "policy",
                "technology" => "policy",
                "connected_app" => "policy",
                "rescue" => "policy",
                "faq" => "policy",
                "fengshui" => "fengshui_color",
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

        private sealed record KnowledgeFact(string Domain, string Slot, string? Brand, string Value)
        {
            public int Index { get; init; }
            public int Score { get; init; }
        }
    }
}
