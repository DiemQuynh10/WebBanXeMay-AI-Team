using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ProductRecommendationService : IProductRecommendationService
    {
        private const int HardRejectScore = -10000;

        // Category weights
        private const int CategoryMatchScore = 32;
        private const int CategoryMismatchPenalty = -8;
        private const int ManualCategoryMismatchPenalty = -10;

        // Price weights
        private const int PriceExactScore = 36;
        private const int PriceNearScore = 30;
        private const int PriceMediumScore = 24;
        private const int PriceLooseScore = 16;
        private const int PriceFarScore = 8;

        private const int PriceTooCheapLowPenalty = -6;
        private const int PriceTooCheapMediumPenalty = -14;
        private const int PriceTooCheapHeavyPenalty = -28;
        private const int PriceTooExpensivePenalty = -10;

        // Usage / style weights
        private const int MaleAgainstFemininePenalty = -14;
        private const int FemaleAgainstAggressivePenalty = -8;
        private const int FemaleManualPenalty = -14;
        private const int FemaleStyleBonus = 6;

        private const int WorkUsageScore = 16;
        private const int CityUsageScore = 12;
        private const int SchoolUsageScore = 12;
        private const int StudentBudgetScore = 12;
        private const int FuelSavingBonus = 12;
        private const int LargeStorageBonus = 12;

        public List<ProductSummaryDto> RankProducts(
    IEnumerable<ProductSummaryDto> products,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string normalizedMessage,
    int take = 5)
        {
            if (products == null)
            {
                return new List<ProductSummaryDto>();
            }

            var message = Normalize(normalizedMessage);
            var requestProfile = BuildRequestProfile(intent, profile, message);

            var rankedCandidates = products
                .Select(p =>
                {
                    var context = BuildProductContext(p);
                    var score = CalculateScore(context, intent, requestProfile);

                    return new ScoredCandidate
                    {
                        Product = p,
                        Context = context,
                        Score = score,
                        PriceDistance = GetPriceDistanceForSort(p, intent)
                    };
                })
                .Where(x => x.Score > HardRejectScore)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.PriceDistance)
                .ThenByDescending(x => x.Product.SoLuong)
                .ThenBy(x => x.Product.Gia)
                .ToList();

            if (rankedCandidates.Count == 0)
            {
                return new List<ProductSummaryDto>();
            }

            var selected = requestProfile.IsOpenConsultation
                ? SelectDiverseProducts(rankedCandidates, Math.Max(1, take))
                : rankedCandidates.Take(Math.Max(1, take)).ToList();

            return selected
                .Select(x => x.Product)
                .ToList();
        }

        private static int CalculateScore(ProductContext product, ParsedIntent intent, RequestProfile profile)
        {
            if (ShouldHardReject(product, intent, profile))
            {
                return HardRejectScore;
            }

            var score = 0;

            score += ScoreByCategory(product, profile);
            score += ScoreByPrice(product, intent, profile);
            score += ScoreByStock(product);
            score += ScoreByPhysicalSpecs(product, profile);
            score += ScoreByUsage(product, profile);
            score += ScoreByStyle(product, profile);
            score += ScoreByExtraNeeds(product, profile);

            return score;
        }

        private static bool ShouldHardReject(ProductContext product, ParsedIntent intent, RequestProfile profile)
        {
            if (product.Stock <= 0)
            {
                return true;
            }

            if (profile.DislikesManual && product.VehicleType == VehicleType.Manual)
            {
                return true;
            }

            if (profile.DislikesScooter && product.VehicleType == VehicleType.Scooter)
            {
                return true;
            }

            if (profile.DislikesUnderbone && product.VehicleType == VehicleType.Underbone)
            {
                return true;
            }

            if (profile.WantsScooter && product.VehicleType != VehicleType.Scooter)
            {
                return true;
            }

            if (profile.WantsUnderbone && product.VehicleType != VehicleType.Underbone)
            {
                return true;
            }

            if (profile.WantsManual && product.VehicleType != VehicleType.Manual)
            {
                return true;
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var diff = Math.Abs(product.Price - target);

                // Nếu user dislike manual thì nới hard reject giá để còn giữ được xe ga/xe số thay thế
                var isStrict = !profile.DislikesManual;
                var maxDiff = target <= 40_000_000m ? 7_000_000m : 8_000_000m;

                if (isStrict && diff > maxDiff)
                {
                    return true;
                }

                if (isStrict && product.Price < target * 0.78m)
                {
                    return true;
                }
            }

            if (intent.PriceMax.HasValue && product.Price > intent.PriceMax.Value + 4_000_000m)
            {
                return true;
            }

            if (intent.PriceMin.HasValue && product.Price < intent.PriceMin.Value - 4_000_000m)
            {
                return true;
            }

            if (profile.PrefersFemaleStyle &&
                profile.IsOpenConsultation &&
                !profile.WantsManual &&
                !profile.RequestedStyles.Contains(StyleTag.Aggressive) &&
                !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
                product.VehicleType == VehicleType.Manual)
            {
                return true;
            }

            return false;
        }

        private static int ScoreByCategory(ProductContext product, RequestProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.PreferredCategory))
            {
                return 0;
            }

            var preferred = profile.PreferredCategory.Replace("xe ", "").Trim();

            if (preferred.Contains("ga"))
            {
                return product.VehicleType == VehicleType.Scooter ? CategoryMatchScore : CategoryMismatchPenalty;
            }

            if (preferred.Contains("so"))
            {
                return product.VehicleType == VehicleType.Underbone ? CategoryMatchScore : CategoryMismatchPenalty;
            }

            if (preferred.Contains("con"))
            {
                return product.VehicleType == VehicleType.Manual ? CategoryMatchScore : ManualCategoryMismatchPenalty;
            }

            return 0;
        }

        private static int ScoreByPrice(ProductContext product, ParsedIntent intent, RequestProfile profile)
        {
            var score = 0;
            var price = product.Price;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var diff = Math.Abs(price - target);
                var ratio = target <= 0 ? 0m : price / target;

                if (diff <= 1_000_000m) score += PriceExactScore;
                else if (diff <= 2_000_000m) score += PriceNearScore;
                else if (diff <= 3_000_000m) score += PriceMediumScore;
                else if (diff <= 4_000_000m) score += PriceLooseScore;
                else if (diff <= 5_000_000m) score += PriceFarScore;
                else if (diff <= 6_000_000m) score -= 6;
                else if (diff <= 10_000_000m) score -= 18;
                else score -= 35;

                if (price < target)
                {
                    if (ratio < 0.85m) score += PriceTooCheapHeavyPenalty;
                    else if (ratio < 0.90m) score += PriceTooCheapMediumPenalty;
                    else if (ratio < 0.95m) score += PriceTooCheapLowPenalty;
                    else score += 3;
                }
                if (ratio <= 1.03m) score += 5;
                else if (ratio <= 1.05m) score += 2;
                else if (ratio <= 1.10m) score -= 8;
                else if (ratio <= 1.20m) score -= 18;
                else score -= 35;

                return score;
            }

            if (intent.PriceMax.HasValue)
            {
                var max = intent.PriceMax.Value;
                if (price <= max)
                {
                    var diff = max - price;
                    score += diff <= 2_000_000m ? 18 : diff <= 5_000_000m ? 12 : 4;
                }
                else
                {
                    var diff = price - max;
                    score += diff <= 1_000_000m ? -2 : diff <= 3_000_000m ? -8 : -18;
                }
            }

            if (intent.PriceMin.HasValue)
            {
                var min = intent.PriceMin.Value;
                if (price >= min)
                {
                    var diff = price - min;
                    score += diff <= 2_000_000m ? 16 : diff <= 5_000_000m ? 10 : 4;
                }
                else
                {
                    var diff = min - price;
                    score += diff <= 1_000_000m ? -2 : diff <= 3_000_000m ? -8 : -18;
                }
            }

            if (!intent.PriceMin.HasValue && !intent.PriceMax.HasValue && profile.IsStudent)
            {
                if (price <= 35_000_000m) score += 8;
                else if (price <= 40_000_000m) score += 4;
            }

            return score;
        }

        private static int ScoreByStock(ProductContext product)
        {
            var stock = product.Stock;
            var score = 0;

            if (stock > 0) score += 10;
            if (stock >= 3) score += 3;
            if (stock >= 5) score += 5;
            if (stock >= 10) score += 3;

            return score;
        }

        private static int ScoreByPhysicalSpecs(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            if (profile.HeightCm.HasValue && product.SeatHeightMm.HasValue)
            {
                var riderHeightMm = profile.HeightCm.Value * 10m;
                var gap = riderHeightMm - product.SeatHeightMm.Value;

                if (gap >= 850m) score += 14;
                else if (gap >= 800m) score += 10;
                else if (gap >= 760m) score += 6;
                else if (gap >= 720m) score += 2;
                else score -= 8;
            }
            if (profile.HeightCm.HasValue && profile.HeightCm.Value <= 150)
            {
                if (ContainsAny(product.Name, "vision", "zip", "latte"))
                {
                    score += 12;
                }

                if (ContainsAny(product.Name, "pcx", "sh", "beverly"))
                {
                    score -= 18;
                }
            }
            if (profile.WantsEasyControl)
            {
                if (product.VehicleType == VehicleType.Scooter)
                {
                    score += 10;
                }

                if (ContainsAny(product.Tags, "de dieu khien", "de di", "linh hoat", "nho gon"))
                {
                    score += 10;
                }
            }

            return score;
        }

        private static int ScoreByUsage(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            if (profile.IsStudent)
            {
                if (product.Price <= 35_000_000m) score += StudentBudgetScore;
                else if (product.Price <= 40_000_000m) score += 8;
                else if (product.Price <= 45_000_000m) score += 3;

                if (product.VehicleType == VehicleType.Underbone) score += 10;
                if (product.VehicleType == VehicleType.Scooter) score += 6;
                if (ContainsAny(product.Tags, "tiet kiem", "di hoc", "ben", "thuc dung")) score += 8;
            }

            if (profile.ForSchool)
            {
                if (ContainsAny(product.Tags, "di hoc", "tiet kiem", "linh hoat", "thuc dung")) score += SchoolUsageScore;
                if (product.VehicleType == VehicleType.Underbone) score += 6;
            }

            if (profile.ForWork)
            {
                if (ContainsAny(product.Tags, "di pho", "thanh lich", "linh hoat", "thuc dung", "trung tinh")) score += WorkUsageScore;
                if (product.VehicleType == VehicleType.Scooter || product.VehicleType == VehicleType.Underbone) score += 6;

                if (profile.PrefersMaleStyle && ContainsAny(product.Tags, "nu tinh")) score += MaleAgainstFemininePenalty;
                if (profile.PrefersFemaleStyle && ContainsAny(product.Tags, "ham ho", "nam tinh")) score += FemaleAgainstAggressivePenalty;
            }

            if (profile.ForCity && ContainsAny(product.Tags, "di pho", "linh hoat", "nho gon", "trung tinh"))
            {
                score += CityUsageScore;
            }

            if (profile.ForTour && ContainsAny(product.Tags, "di tour", "duong dai", "manh me"))
            {
                score += 12;
            }

            if (profile.PrefersFemaleStyle)
            {
                if (product.VehicleType == VehicleType.Manual)
                {
                    score += FemaleManualPenalty;
                }

                if (ContainsAny(product.Tags, "nu tinh", "thanh lich", "nhe nhang", "de di"))
                {
                    score += FemaleStyleBonus;
                }
            }

            if (profile.PrefersMaleStyle)
            {
                if (product.VehicleType == VehicleType.Manual &&
                    !profile.RequestedStyles.Contains(StyleTag.Aggressive) &&
                    !profile.RequestedStyles.Contains(StyleTag.Sporty))
                {
                    score -= 4;
                }
            }

            return score;
        }

        private static int ScoreByStyle(ProductContext product, RequestProfile profile)
        {
            return CalculateStyleMatch(product, profile);
        }

        private static int ScoreByExtraNeeds(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            if (profile.WantsFuelSaving && ContainsAny(product.Tags, "tiet kiem", "it ton xang"))
            {
                score += FuelSavingBonus;
            }

            if (profile.WantsLargeStorage && ContainsAny(product.Tags, "cop rong", "de do", "chua do"))
            {
                score += LargeStorageBonus;
            }

            return score;
        }

        private static int CalculateStyleMatch(ProductContext product, RequestProfile profile)
        {
            if (profile.RequestedStyles.Count == 0)
            {
                return 0;
            }

            var score = 0;

            foreach (var style in profile.RequestedStyles)
            {
                if (style == StyleTag.Sporty &&
                    ContainsAny(product.Tags, "the thao", "nang dong", "ca tinh"))
                {
                    score += 14;
                }

                if (style == StyleTag.Elegant &&
                    ContainsAny(product.Tags, "thanh lich", "nhe nhang", "sang"))
                {
                    score += 12;
                }

                if (style == StyleTag.Aggressive &&
                    ContainsAny(product.Tags, "ham ho", "manh me", "ca tinh", "the thao"))
                {
                    score += 16;
                }

                if (style == StyleTag.Compact &&
                    ContainsAny(product.Tags, "linh hoat", "nho gon", "gon", "de di"))
                {
                    score += 10;
                }
            }

            if (profile.PrefersMaleStyle)
            {
                if (ContainsAny(product.Tags, "nam tinh", "the thao", "trung tinh")) score += 7;
                if (ContainsAny(product.Tags, "nu tinh")) score -= 6;
            }

            if (profile.PrefersFemaleStyle)
            {
                if (ContainsAny(product.Tags, "nu tinh", "thanh lich", "nhe nhang", "trung tinh")) score += 7;
                if (ContainsAny(product.Tags, "ham ho")) score -= 6;
            }

            if (profile.RequestedStyles.Contains(StyleTag.Aggressive))
            {
                if (ContainsAny(product.Tags, "nu tinh", "nhe nhang"))
                {
                    score -= 8;
                }
            }

            if (profile.ForCity && ContainsAny(product.Tags, "di pho", "linh hoat", "nho gon"))
            {
                score += 6;
            }

            if (profile.ForTour && ContainsAny(product.Tags, "di tour", "duong dai", "manh me"))
            {
                score += 6;
            }

            return score;
        }

        private static RequestProfile BuildRequestProfile(
    ParsedIntent intent,
    CustomerPreferenceProfile conversationProfile,
    string message)
        {
            var target = Normalize(intent.Target);
            var preferredCategory = Normalize(intent.Category);

            if (string.IsNullOrWhiteSpace(preferredCategory) && !string.IsNullOrWhiteSpace(conversationProfile.PreferredCategory))
            {
                preferredCategory = Normalize(conversationProfile.PreferredCategory);
            }

            if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(conversationProfile.Target))
            {
                target = Normalize(conversationProfile.Target);
            }

            var dislikesManual =
                conversationProfile.ExcludedCategories.Contains("côn tay") ||
                ContainsAny(message, "khong thich xe con", "khong thich con", "khong muon xe con", "ne xe con", "ghet xe con");

            var dislikesScooter =
                conversationProfile.ExcludedCategories.Contains("xe ga") ||
                ContainsAny(message, "khong thich xe ga", "khong muon xe ga", "ne xe ga", "ghet xe ga");

            var dislikesUnderbone =
                conversationProfile.ExcludedCategories.Contains("xe số") ||
                ContainsAny(message, "khong thich xe so", "khong muon xe so", "ne xe so", "ghet xe so");

            var profile = new RequestProfile
            {
                RawMessage = message,
                PreferredCategory = string.IsNullOrWhiteSpace(preferredCategory) ? null : preferredCategory,

                IsStudent =
                    ContainsAny(target, "sinh vien", "sinhvien") ||
                    ContainsAny(message, "sinh vien", "sinhvien"),

                ForSchool = conversationProfile.ForSchool || ContainsAny(message, "di hoc", "den truong", "hoc hang ngay"),
                ForWork = conversationProfile.ForWork || ContainsAny(message, "di lam", "cong so", "di lam hang ngay"),
                ForCity = conversationProfile.ForCity || ContainsAny(message, "di pho", "trong pho", "do thi", "di hang ngay"),
                ForTour = conversationProfile.ForTour || ContainsAny(message, "di tour", "duong dai", "di xa", "phuot"),

                WantsEasyControl = conversationProfile.WantsEasyControl || ContainsAny(message, "de di", "de dieu khien", "nhe", "gon", "linh hoat"),
                WantsFuelSaving = conversationProfile.WantsFuelSaving || ContainsAny(message, "tiet kiem xang", "it ton xang"),
                WantsLargeStorage = conversationProfile.WantsLargeStorage || ContainsAny(message, "cop rong", "de do", "chua do"),

                WantsScooter = !dislikesScooter &&
                    ((ContainsAny(message, "muon xe ga", "thich xe ga", "chon xe ga", "tay ga", "xe ga") || preferredCategory.Contains("ga"))),

                WantsUnderbone = !dislikesUnderbone &&
                    ((ContainsAny(message, "muon xe so", "thich xe so", "chon xe so", "xe so", "xe số") || preferredCategory.Contains("so"))),

                WantsManual = !dislikesManual &&
                    ((ContainsAny(message, "muon xe con", "chon xe con", "thich xe con", "xe con tay", "muon con tay", "thich con tay", "chon con tay")
                     || preferredCategory.Contains("con"))),

                DislikesManual = dislikesManual,
                DislikesScooter = dislikesScooter,
                DislikesUnderbone = dislikesUnderbone,

                PrefersMaleStyle = conversationProfile.PrefersMaleStyle || ContainsAny(target, "nam") || ContainsAny(message, "cho nam", "nam di lam", "nam di pho"),
                PrefersFemaleStyle = conversationProfile.PrefersFemaleStyle || ContainsAny(target, "nu") || ContainsAny(message, "cho nu", "nu di lam", "nu di pho"),

                IsOpenConsultation = true,

                HeightCm = conversationProfile.HeightCm ?? ExtractHeightCm(message),
                RequestedStyles = MergeStyles(conversationProfile.RequestedStyles, ExtractRequestedStyles(message))
            };

            return profile;
        }
        private static HashSet<StyleTag> MergeStyles(
    IEnumerable<string> profileStyles,
    HashSet<StyleTag> messageStyles)
        {
            var result = new HashSet<StyleTag>(messageStyles);

            foreach (var style in profileStyles)
            {
                switch (Normalize(style))
                {
                    case "sporty":
                        result.Add(StyleTag.Sporty);
                        break;
                    case "elegant":
                        result.Add(StyleTag.Elegant);
                        break;
                    case "aggressive":
                        result.Add(StyleTag.Aggressive);
                        break;
                    case "compact":
                        result.Add(StyleTag.Compact);
                        break;
                }
            }

            return result;
        }

        private static ProductContext BuildProductContext(ProductSummaryDto product)
        {
            var name = Normalize(product.Ten);
            var brand = Normalize(product.ThuongHieu);
            var category = Normalize(product.Loai);
            var vehicleType = DetectVehicleType(name, category);

            return new ProductContext
            {
                Name = name,
                Brand = brand,
                Category = category,
                VehicleType = vehicleType,
                Tags = InferTags(name, brand, category, vehicleType),
                SeatHeightMm = InferSeatHeightMm(name, category, vehicleType, product.CC),
                Price = product.Gia,
                Stock = product.SoLuong,
                EngineCc = product.CC
            };
        }

        private static HashSet<string> InferTags(
            string name,
            string brand,
            string category,
            VehicleType vehicleType)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (vehicleType == VehicleType.Scooter)
            {
                tags.Add("di pho");
                tags.Add("linh hoat");
                tags.Add("de dieu khien");
            }

            if (vehicleType == VehicleType.Underbone)
            {
                tags.Add("tiet kiem");
                tags.Add("di hoc");
                tags.Add("ben");
                tags.Add("thuc dung");
            }

            if (vehicleType == VehicleType.Manual)
            {
                tags.Add("the thao");
                tags.Add("di tour");
                tags.Add("manh me");
            }

            if (ContainsAny(name, "air blade", "vario"))
            {
                tags.Add("the thao");
                tags.Add("ca tinh");
                tags.Add("nam tinh");
                tags.Add("trung tinh");
            }

            if (ContainsAny(name, "vision", "janus", "latte", "grande", "zip"))
            {
                tags.Add("nu tinh");
                tags.Add("thanh lich");
                tags.Add("de di");
            }

            if (ContainsAny(name, "vision"))
            {
                tags.Add("trung tinh");
                tags.Add("thuc dung");
            }

            if (ContainsAny(name, "janus", "latte", "grande"))
            {
                tags.Add("nhe nhang");
            }

            if (ContainsAny(name, "zip"))
            {
                tags.Add("nho gon");
                tags.Add("di pho");
            }

            if (ContainsAny(name, "lead", "freego"))
            {
                tags.Add("cop rong");
                tags.Add("di pho");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
            }

            if (ContainsAny(name, "wave", "sirius", "jupiter", "future"))
            {
                tags.Add("tiet kiem");
                tags.Add("di hoc");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
            }

            if (ContainsAny(name, "winner", "exciter"))
            {
                tags.Add("the thao");
                tags.Add("manh me");
                tags.Add("nam tinh");
            }

            if (ContainsAny(name, "galaxy"))
            {
                tags.Add("thuc dung");
                tags.Add("tiet kiem");
            }

            if (ContainsAny(brand, "yamaha"))
            {
                tags.Add("nang dong");
            }

            if (ContainsAny(category, "ga"))
            {
                tags.Add("de dieu khien");
            }

            return tags;
        }

        private static decimal? InferSeatHeightMm(
            string name,
            string category,
            VehicleType vehicleType,
            short? cc)
        {
            if (vehicleType == VehicleType.Manual)
            {
                return 790m;
            }

            if (ContainsAny(name, "vision", "janus", "latte", "zip"))
            {
                return 760m;
            }

            if (ContainsAny(name, "lead", "freego", "air blade", "vario"))
            {
                return 780m;
            }

            if (vehicleType == VehicleType.Underbone)
            {
                return 770m;
            }

            if (vehicleType == VehicleType.Scooter)
            {
                return 775m;
            }

            if (cc.HasValue && cc.Value >= 150)
            {
                return 790m;
            }

            return null;
        }

        private static VehicleType DetectVehicleType(string name, string category)
        {
            if (ContainsAny(category, "con", "côn", "con tay", "côn tay") ||
                ContainsAny(name, "exciter", "winner", "husky"))
            {
                return VehicleType.Manual;
            }

            if (ContainsAny(category, "so", "số", "xe so", "xe số") ||
                ContainsAny(name, "wave", "sirius", "jupiter", "future", "galaxy"))
            {
                return VehicleType.Underbone;
            }

            if (ContainsAny(category, "ga", "tay ga", "xe ga") ||
                ContainsAny(name, "vision", "janus", "latte", "lead", "zip", "freego", "grande", "air blade", "vario"))
            {
                return VehicleType.Scooter;
            }

            return VehicleType.Unknown;
        }

        private static int? ExtractHeightCm(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var patterns = new[]
            {
                @"cao\s*(\d{3})\s*cm",
                @"1m(\d{2})",
                @"m(\d{2})",
                @"(\d{3})\s*cm"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                if (pattern == @"1m(\d{2})" || pattern == @"m(\d{2})")
                {
                    if (int.TryParse(match.Groups[1].Value, out var sub))
                    {
                        return 100 + sub;
                    }
                }
                else
                {
                    if (int.TryParse(match.Groups[1].Value, out var cm) && cm >= 120 && cm <= 220)
                    {
                        return cm;
                    }
                }
            }

            return null;
        }

        private static HashSet<StyleTag> ExtractRequestedStyles(string message)
        {
            var styles = new HashSet<StyleTag>();

            if (ContainsAny(message, "the thao", "nang dong"))
            {
                styles.Add(StyleTag.Sporty);
            }

            if (ContainsAny(message, "thanh lich", "nha nhan", "nhe nhang", "sang"))
            {
                styles.Add(StyleTag.Elegant);
            }

            if (ContainsAny(message, "ham ho", "manh me"))
            {
                styles.Add(StyleTag.Aggressive);
            }

            if (ContainsAny(message, "ca tinh"))
            {
                styles.Add(StyleTag.Sporty);
                styles.Add(StyleTag.Aggressive);
            }

            if (ContainsAny(message, "nho gon", "gon", "linh hoat"))
            {
                styles.Add(StyleTag.Compact);
            }

            return styles;
        }

        private static decimal GetPriceDistanceForSort(ProductSummaryDto product, ParsedIntent intent)
        {
            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                return Math.Abs(product.Gia - intent.TargetPrice.Value);
            }

            if (intent.PriceMax.HasValue && product.Gia > intent.PriceMax.Value)
            {
                return product.Gia - intent.PriceMax.Value;
            }

            if (intent.PriceMin.HasValue && product.Gia < intent.PriceMin.Value)
            {
                return intent.PriceMin.Value - product.Gia;
            }

            return 0m;
        }

        private static List<ScoredCandidate> SelectDiverseProducts(List<ScoredCandidate> rankedCandidates, int take)
        {
            var selected = new List<ScoredCandidate>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in rankedCandidates)
            {
                if (selected.Count >= take)
                {
                    break;
                }

                if (usedNames.Contains(candidate.Context.Name))
                {
                    continue;
                }

                selected.Add(candidate);
                usedNames.Add(candidate.Context.Name);
            }

            return selected;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            var normalized = Normalize(text);

            foreach (var keyword in keywords)
            {
                var key = Normalize(keyword);
                if (!string.IsNullOrWhiteSpace(key) && normalized.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(IEnumerable<string> texts, params string[] keywords)
        {
            foreach (var text in texts)
            {
                if (ContainsAny(text, keywords))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var value = text.Trim().ToLowerInvariant();

            value = value
                .Replace("đ", "d")
                .Replace("riệu", "trieu")
                .Replace("triệu", "trieu")
                .Replace("triệu", "trieu")
                .Replace("  ", " ");

            value = RemoveVietnameseSigns(value);

            while (value.Contains("  "))
            {
                value = value.Replace("  ", " ");
            }

            return value;
        }

        private static string RemoveVietnameseSigns(string text)
        {
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

            var chars = text.Select(c => map.ContainsKey(c) ? map[c] : c).ToArray();
            return new string(chars);
        }

        private sealed class ScoredCandidate
        {
            public ProductSummaryDto Product { get; set; } = new ProductSummaryDto();
            public ProductContext Context { get; set; } = new ProductContext();
            public int Score { get; set; }
            public decimal PriceDistance { get; set; }
        }

        private sealed class ProductContext
        {
            public string Name { get; set; } = string.Empty;
            public string Brand { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public VehicleType VehicleType { get; set; } = VehicleType.Unknown;
            public HashSet<string> Tags { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public decimal? SeatHeightMm { get; set; }
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public short? EngineCc { get; set; }
        }

        private sealed class RequestProfile
        {
            public string RawMessage { get; set; } = string.Empty;
            public string? PreferredCategory { get; set; }
            public bool IsStudent { get; set; }
            public bool ForSchool { get; set; }
            public bool ForWork { get; set; }
            public bool ForCity { get; set; }
            public bool ForTour { get; set; }
            public bool WantsEasyControl { get; set; }
            public bool WantsFuelSaving { get; set; }
            public bool WantsLargeStorage { get; set; }
            public bool WantsScooter { get; set; }
            public bool WantsUnderbone { get; set; }
            public bool WantsManual { get; set; }
            public bool DislikesManual { get; set; }
            public bool DislikesScooter { get; set; }
            public bool DislikesUnderbone { get; set; }
            public bool PrefersMaleStyle { get; set; }
            public bool PrefersFemaleStyle { get; set; }
            public bool IsOpenConsultation { get; set; }
            public int? HeightCm { get; set; }
            public HashSet<StyleTag> RequestedStyles { get; set; } = new HashSet<StyleTag>();
        }

        private enum VehicleType
        {
            Unknown = 0,
            Scooter = 1,
            Underbone = 2,
            Manual = 3
        }

        private enum StyleTag
        {
            Sporty = 1,
            Elegant = 2,
            Aggressive = 3,
            Compact = 4
        }
    }
}