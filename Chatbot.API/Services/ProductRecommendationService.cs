using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ProductRecommendationService : IProductRecommendationService
    {
        private const int HardRejectScore = -10000;

        // Category
        private const int CategoryMatchScore = 32;
        private const int CategoryMismatchPenalty = -10;

        // Price
        private const int PriceExactScore = 38;
        private const int PriceNearScore = 30;
        private const int PriceMediumScore = 22;
        private const int PriceLooseScore = 12;
        private const int PriceFarPenalty = -8;
        private const int PriceVeryFarPenalty = -20;
        private const int PriceTooCheapPenalty = -10;
        private const int PriceWayTooCheapPenalty = -22;
        private const int PriceTooExpensivePenalty = -14;
        private const int PriceWayTooExpensivePenalty = -28;

        // Stock
        private const int InStockScore = 10;
        private const int GoodStockScore = 4;
        private const int HighStockScore = 4;

        // Physical / control
        private const int CompactFitStrongBonus = 18;
        private const int CompactFitBonus = 10;
        private const int CompactMismatchPenalty = -10;
        private const int LowSeatStrongBonus = 18;
        private const int LowSeatBonus = 10;
        private const int LowSeatMismatchPenalty = -12;
        private const int EasyControlBonus = 12;

        // Usage
        private const int StudentBudgetBonus = 14;
        private const int SchoolUsageBonus = 12;
        private const int WorkUsageBonus = 14;
        private const int CityUsageBonus = 10;
        private const int TourUsageBonus = 10;

        // Needs
        private const int FuelSavingBonus = 12;
        private const int LargeStorageBonus = 18;

        // Style
        private const int FemaleStyleBonus = 18;
        private const int MaleStyleBonus = 12;
        private const int FemaleAgainstAggressivePenalty = -12;
        private const int MaleAgainstFemininePenalty = -12;
        private const int SportyBonus = 18;
        private const int ElegantBonus = 16;
        private const int AggressiveBonus = 18;
        private const int CompactStyleBonus = 12;

        // Vehicle type preference
        private const int ScooterForCompactBonus = 8;
        private const int ScooterForFemaleBonus = 12;
        private const int UnderboneForStudentBonus = 5;
        private const int ManualAgainstSoftNeedsPenalty = -12;

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
            score += ScoreByExtraNeeds(product, profile);
            score += ScoreByStyle(product, profile);
            score += ScoreByBrandPreference(product, profile);
            score += ScoreByVehicleTypeAffinity(product, profile);
            score += ScoreByEngineCc(product, profile);

            var followUpFeatureScore = ScoreByFollowUpFeature(product, intent);

            if (!string.IsNullOrWhiteSpace(intent.ComparisonFeature))
            {
                score += followUpFeatureScore * 2;
            }
            else
            {
                score += followUpFeatureScore;
            }

            return score;
        }
        private static int ScoreByFollowUpFeature(ProductContext product, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(intent.ComparisonFeature))
                return 0;

            return intent.ComparisonFeature switch
            {
                "storage" => ScoreStorageFeature(product),
                "low_seat" => ScoreLowSeatFeature(product),
                "female_fit" => ScoreFemaleFitFeature(product),
                "fuel_saving" => ScoreFuelSavingFeature(product),
                "work_fit" => ScoreWorkFitFeature(product),
                "school_fit" => ScoreSchoolFitFeature(product),
                "ride_comfort" => ScoreRideComfortFeature(product),
                _ => 0
            };
        }

        private static int ScoreStorageFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "cop rong", "de do", "chua do"))
                score += 30;

            if (ContainsAny(product.Name, "freego", "lead"))
                score += 16;

            if (ContainsAny(product.Name, "latte"))
                score += 10;

            return score;
        }

        private static int ScoreLowSeatFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "yen thap", "de chong chan"))
                score += 32;

            if (product.SeatHeightMm.HasValue && product.SeatHeightMm.Value <= 770m)
                score += 16;

            if (ContainsAny(product.Name, "zip"))
                score += 12;

            if (ContainsAny(product.Name, "vision", "latte"))
                score += 8;

            return score;
        }

        private static int ScoreFemaleFitFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "nu tinh", "thanh lich", "nhe nhang", "de di"))
                score += 28;

            if (product.VehicleType == VehicleType.Scooter)
                score += 8;

            if (ContainsAny(product.Name, "latte", "grande", "attila", "venus"))
                score += 12;

            if (ContainsAny(product.Name, "vision", "zip"))
                score += 8;

            if (ContainsAny(product.Tags, "ham ho", "manh me", "dam chac"))
                score -= 10;

            return score;
        }

        private static int ScoreFuelSavingFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "tiet kiem", "it ton xang"))
                score += 28;

            if (ContainsAny(product.Name, "wave", "future", "sirius", "vision"))
                score += 10;

            return score;
        }

        private static int ScoreWorkFitFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "di lam", "thuc dung", "trung tinh", "linh hoat", "dam chac"))
                score += 24;

            if (ContainsAny(product.Tags, "nu tinh", "nhe nhang") &&
                !ContainsAny(product.Tags, "trung tinh", "thuc dung"))
            {
                score -= 10;
            }

            if (ContainsAny(product.Name, "zip"))
            {
                score -= 6;
            }

            if (ContainsAny(product.Name, "air blade"))
                score += 18;

            if (ContainsAny(product.Name, "burgman"))
                score += 14;

            if (ContainsAny(product.Name, "freego", "future"))
                score += 10;

            if (ContainsAny(product.Name, "latte"))
                score += 4;

            return score;
        }

        private static int ScoreSchoolFitFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "di hoc", "tiet kiem", "de di", "thuc dung"))
                score += 24;

            if (ContainsAny(product.Name, "vision", "wave", "sirius"))
                score += 10;

            return score;
        }
        private static int ScoreRideComfortFeature(ProductContext product)
        {
            var score = 0;

            if (ContainsAny(product.Tags, "de di", "nhe nhang", "di pho", "linh hoat"))
                score += 18;

            if (ContainsAny(product.Name, "grande", "latte", "vision"))
                score += 12;

            if (ContainsAny(product.Tags, "dam chac"))
                score += 6;

            if (product.VehicleType == VehicleType.Scooter)
                score += 4;

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

                var maxDiff = profile.NeedsCompactFit || profile.NeedsLowSeat
                    ? 9_000_000m
                    : 8_000_000m;

                if (diff > maxDiff)
                {
                    return true;
                }

                if (!profile.IsStudent && !profile.ForSchool && product.Price < target * 0.72m)
                {
                    return true;
                }
            }

            if (intent.PriceMax.HasValue && product.Price > intent.PriceMax.Value + 5_000_000m)
            {
                return true;
            }

            if (intent.PriceMin.HasValue && product.Price < intent.PriceMin.Value - 5_000_000m)
            {
                return true;
            }

            if ((profile.NeedsCompactFit || profile.NeedsLowSeat) &&
                product.VehicleType == VehicleType.Manual &&
                !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
                !profile.RequestedStyles.Contains(StyleTag.Aggressive))
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
                return product.VehicleType == VehicleType.Manual ? CategoryMatchScore : CategoryMismatchPenalty;
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
                else if (diff <= 4_000_000m) score += PriceMediumScore;
                else if (diff <= 6_000_000m) score += PriceLooseScore;
                else if (diff <= 8_000_000m) score += PriceFarPenalty;
                else score += PriceVeryFarPenalty;

                if (price < target)
                {
                    if (ratio < 0.78m) score += PriceWayTooCheapPenalty;
                    else if (ratio < 0.88m) score += PriceTooCheapPenalty;
                    else score += 3;
                }
                else
                {
                    if (ratio <= 1.03m) score += 5;
                    else if (ratio <= 1.08m) score += 1;
                    else if (ratio <= 1.12m) score += PriceTooExpensivePenalty;
                    else score += PriceWayTooExpensivePenalty;
                }

                if ((profile.IsStudent || profile.ForSchool) && price <= target && price <= 40_000_000m)
                {
                    score += 4;
                }

                return score;
            }

            if (intent.PriceMax.HasValue)
            {
                var max = intent.PriceMax.Value;
                var diff = price - max;

                if (price <= max)
                {
                    var below = max - price;
                    score += below <= 2_000_000m ? 18 :
                             below <= 5_000_000m ? 12 :
                             4;
                }
                else
                {
                    score += diff <= 1_000_000m ? -3 :
                             diff <= 3_000_000m ? -10 :
                             -22;
                }
            }

            if (intent.PriceMin.HasValue)
            {
                var min = intent.PriceMin.Value;
                var diff = min - price;

                if (price >= min)
                {
                    var above = price - min;
                    score += above <= 2_000_000m ? 16 :
                             above <= 5_000_000m ? 10 :
                             4;
                }
                else
                {
                    score += diff <= 1_000_000m ? -3 :
                             diff <= 3_000_000m ? -10 :
                             -22;
                }
            }

            if (!intent.PriceMin.HasValue && !intent.PriceMax.HasValue && !intent.TargetPrice.HasValue)
            {
                if (profile.IsStudent)
                {
                    if (price <= 35_000_000m) score += 10;
                    else if (price <= 40_000_000m) score += 6;
                    else if (price <= 45_000_000m) score += 2;
                }
            }

            return score;
        }

        private static int ScoreByStock(ProductContext product)
        {
            var score = 0;

            if (product.Stock > 0) score += InStockScore;
            if (product.Stock >= 3) score += GoodStockScore;
            if (product.Stock >= 7) score += HighStockScore;

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

            if (profile.NeedsCompactFit)
            {
                if (ContainsAny(product.Tags, "nho gon", "gon", "linh hoat"))
                {
                    score += CompactFitStrongBonus;
                }
                else if (product.VehicleType == VehicleType.Scooter)
                {
                    score += CompactFitBonus;
                }

                if (ContainsAny(product.Tags, "dam chac", "to lon", "ham ho"))
                {
                    score += CompactMismatchPenalty;
                }
            }

            if (profile.NeedsLowSeat)
            {
                if (ContainsAny(product.Tags, "yen thap", "de chong chan"))
                {
                    score += LowSeatStrongBonus;
                }
                else if (product.SeatHeightMm.HasValue && product.SeatHeightMm.Value <= 770m)
                {
                    score += LowSeatBonus;
                }
                else if (product.SeatHeightMm.HasValue && product.SeatHeightMm.Value >= 785m)
                {
                    score += LowSeatMismatchPenalty;
                }
            }

            if (profile.WantsEasyControl)
            {
                if (ContainsAny(product.Tags, "de dieu khien", "de di", "linh hoat"))
                {
                    score += EasyControlBonus;
                }
                else if (product.VehicleType == VehicleType.Scooter)
                {
                    score += 6;
                }
            }

            if (profile.HeightCm.HasValue && profile.HeightCm.Value <= 150)
            {
                if (ContainsAny(product.Tags, "yen thap", "de chong chan", "nho gon"))
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
                if (product.Price <= 35_000_000m) score += StudentBudgetBonus;
                else if (product.Price <= 40_000_000m) score += 8;
                else if (product.Price <= 45_000_000m) score += 3;

                if (ContainsAny(product.Tags, "di hoc", "tiet kiem", "thuc dung", "de di"))
                {
                    score += 10;
                }

                if (product.VehicleType == VehicleType.Underbone)
                {
                    score += UnderboneForStudentBonus;
                }
                else if (product.VehicleType == VehicleType.Scooter)
                {
                    score += 8;
                }

                if (profile.PrefersFemaleStyle && product.VehicleType == VehicleType.Scooter)
                {
                    score += 8;
                }

            }

            if (profile.ForSchool)
            {
                if (ContainsAny(product.Tags, "di hoc", "tiet kiem", "linh hoat", "thuc dung"))
                {
                    score += SchoolUsageBonus;
                }
            }

            if (profile.ForWork)
            {
                if (ContainsAny(product.Tags, "di pho", "thuc dung", "trung tinh", "linh hoat", "dam chac"))
                {
                    score += WorkUsageBonus;
                }

                if (product.VehicleType == VehicleType.Scooter || product.VehicleType == VehicleType.Underbone)
                {
                    score += 4;
                }

                if (profile.PrefersMaleStyle && ContainsAny(product.Tags, "nu tinh", "nhe nhang"))
                {
                    score -= 18;
                }
                if (profile.PrefersMaleStyle &&
    ContainsAny(product.Tags, "trung tinh", "dam chac", "di lam", "thuc dung"))
                {
                    score += 12;
                }

                if (profile.PrefersFemaleStyle && ContainsAny(product.Tags, "ham ho", "manh me", "dam chac"))
                {
                    score -= 12;
                }
                if (profile.PrefersFemaleStyle &&
    ContainsAny(product.Tags, "de di", "de dieu khien", "linh hoat", "di pho"))
                {
                    score += 8;
                }
            }

            if (profile.ForCity && ContainsAny(product.Tags, "di pho", "linh hoat", "nho gon", "trung tinh"))
            {
                score += CityUsageBonus;
            }

            if (profile.ForTour && ContainsAny(product.Tags, "di tour", "duong dai", "manh me"))
            {
                score += TourUsageBonus;
            }

            return score;
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

        private static int ScoreByStyle(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            if (profile.PrefersFemaleStyle)
            {
                if (ContainsAny(product.Tags, "nu tinh", "thanh lich", "nhe nhang", "trung tinh", "de di"))
                {
                    score += FemaleStyleBonus;
                }

                    if (ContainsAny(product.Tags, "ham ho", "qua to", "dam chac", "manh me"))
                {
                    score += FemaleAgainstAggressivePenalty;
                }


                if (product.VehicleType == VehicleType.Manual &&
                    !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
                    !profile.RequestedStyles.Contains(StyleTag.Aggressive))
                {
                    score -= 14;
                }


                if (ContainsAny(product.Tags, "nam tinh") &&
                    !profile.RequestedStyles.Contains(StyleTag.Sporty))
                {
                    score -= 10;
                }
                if (ContainsAny(product.Tags, "ca tinh", "the thao") &&
    !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
    !profile.RequestedStyles.Contains(StyleTag.Aggressive))
                {
                    score -= 4;
                }
            }

            if (profile.PrefersMaleStyle)
            {
                if (ContainsAny(product.Tags, "nam tinh", "the thao", "trung tinh", "dam chac"))
                {
                    score += MaleStyleBonus;
                }

                if (ContainsAny(product.Tags, "nu tinh", "nhe nhang", "thanh lich"))
                {
                    score -= 16;
                }

                if (profile.ForWork && ContainsAny(product.Tags, "nu tinh", "nhe nhang", "thanh lich"))
                {
                    score -= 24;
                }

                if (profile.ForWork && ContainsAny(product.Tags, "trung tinh", "dam chac", "di lam", "thuc dung"))
                {
                    score += 10;
                }
                if (profile.PrefersMaleStyle && profile.ForWork &&
    product.VehicleType == VehicleType.Scooter &&
    ContainsAny(product.Tags, "trung tinh", "dam chac", "di lam", "thuc dung"))
                {
                    score += 10;
                }
            }

            foreach (var style in profile.RequestedStyles)
            {
                if (style == StyleTag.Sporty &&
                    ContainsAny(product.Tags, "the thao", "nang dong", "ca tinh"))
                {
                    score += SportyBonus;
                }

                if (style == StyleTag.Elegant &&
                    ContainsAny(product.Tags, "thanh lich", "nhe nhang", "sang", "nu tinh"))
                {
                    score += ElegantBonus;
                }

                if (style == StyleTag.Aggressive &&
                    ContainsAny(product.Tags, "ham ho", "manh me", "ca tinh", "the thao", "dam chac"))
                {
                    score += AggressiveBonus;
                }

                if (style == StyleTag.Compact &&
                    ContainsAny(product.Tags, "nho gon", "gon", "linh hoat", "de di", "de dieu khien"))
                {
                    score += CompactStyleBonus;
                }
            }

            if (profile.NeedsCompactFit || profile.NeedsLowSeat)
            {
                if (product.VehicleType == VehicleType.Manual &&
                    !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
                    !profile.RequestedStyles.Contains(StyleTag.Aggressive))
                {
                    score += ManualAgainstSoftNeedsPenalty;
                }

                if (ContainsAny(product.Tags, "dam chac", "to lon"))
                {
                    score -= 8;
                }
            }

            if (profile.RequestedStyles.Contains(StyleTag.Sporty) || profile.RequestedStyles.Contains(StyleTag.Aggressive))
            {
                if (ContainsAny(product.Tags, "nu tinh", "nhe nhang") &&
                    !ContainsAny(product.Tags, "trung tinh"))
                {
                    score -= 10;
                }
            }
            if (profile.PrefersMaleStyle && profile.ForWork)
            {
                if (product.VehicleType == VehicleType.Manual &&
                    !profile.RequestedStyles.Contains(StyleTag.Sporty) &&
                    !profile.RequestedStyles.Contains(StyleTag.Aggressive))
                {
                    score -= 6;
                }
            }

            if (profile.PrefersFemaleStyle)
            {
                if (product.VehicleType == VehicleType.Scooter)
                {
                    score += 6;
                }

                if (product.VehicleType == VehicleType.Underbone &&
                    !profile.IsStudent &&
                    !profile.ForSchool)
                {
                    score -= 4;
                }
            }
            return score;
        }

        private static int ScoreByBrandPreference(ProductContext product, RequestProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.PreferredBrand))
            {
                return 0;
            }

            return string.Equals(product.Brand, profile.PreferredBrand, StringComparison.OrdinalIgnoreCase)
                ? 14
                : 0;
        }

        private static int ScoreByVehicleTypeAffinity(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            if (profile.NeedsCompactFit && product.VehicleType == VehicleType.Scooter)
            {
                score += ScooterForCompactBonus;
            }

            if (profile.PrefersFemaleStyle && product.VehicleType == VehicleType.Scooter)
            {
                score += ScooterForFemaleBonus;
            }
            if (profile.PrefersFemaleStyle &&
    product.VehicleType == VehicleType.Underbone &&
    !profile.WantsUnderbone)
            {
                score -= 8;
            }

            if ((profile.NeedsCompactFit || profile.NeedsLowSeat) &&
                product.VehicleType == VehicleType.Underbone &&
                !profile.PrefersFemaleStyle)
            {
                score += 4;
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
            var preferredBrand = Normalize(intent.Brand);

            if (string.IsNullOrWhiteSpace(preferredCategory) && !string.IsNullOrWhiteSpace(conversationProfile.PreferredCategory))
            {
                preferredCategory = Normalize(conversationProfile.PreferredCategory);
            }

            if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(conversationProfile.Target))
            {
                target = Normalize(conversationProfile.Target);
            }

            if (string.IsNullOrWhiteSpace(preferredBrand) && !string.IsNullOrWhiteSpace(conversationProfile.PreferredBrand))
            {
                preferredBrand = Normalize(conversationProfile.PreferredBrand);
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
            var explicitlyWants50cc =
    ContainsAny(message,
        "50cc", "xe 50", "xe 50cc",
        "chua co bang", "chưa có bằng",
        "hoc sinh", "học sinh",
        "khong can bang", "không cần bằng");
            var explicitlyWantsScooter =
    ContainsAny(message, "muon xe ga", "thich xe ga", "chon xe ga", "tay ga");

            var explicitlyWantsUnderbone =
                ContainsAny(message, "muon xe so", "thich xe so", "chon xe so");

            var explicitlyWantsManual =
                ContainsAny(message, "muon xe con", "chon xe con", "thich xe con", "xe con tay", "muon con tay", "thich con tay", "chon con tay");

            var needsCompactFit =
                ContainsAny(message,
                    "dang nguoi nho", "dang nguoi nho gon", "voc dang nho", "nguoi nho", "nho con",
                    "nho gon", "gon nhe", "xe gon", "dang gon", "than hinh nho",
                    "nguoi be", "nho nhan", "xe dung qua to", "xe khong qua to")
                || conversationProfile.NeedsLowSeat
                || (conversationProfile.HeightCm.HasValue && conversationProfile.HeightCm.Value <= 155);

            var needsLowSeat =
                conversationProfile.NeedsLowSeat
                || ContainsAny(message,
                    "de chong chan", "yen thap", "chan ngan", "nguoi thap", "thap", "de xuong chan",
                    "xe thap", "de cham chan", "yen khong cao", "de dung chan");

            var wantsEasyControl =
                conversationProfile.WantsEasyControl ||
                ContainsAny(message,
                    "de di", "de dieu khien", "nhe", "linh hoat", "de xoay tro",
                    "de quay dau", "de dat", "de dung", "de lam quen");

            var requestedStyles = MergeStyles(conversationProfile.RequestedStyles, ExtractRequestedStyles(message));

            return new RequestProfile
            {
                RawMessage = message,
                PreferredCategory = string.IsNullOrWhiteSpace(preferredCategory) ? null : preferredCategory,
                PreferredBrand = string.IsNullOrWhiteSpace(preferredBrand) ? null : preferredBrand,

                IsStudent =
                    ContainsAny(target, "sinh vien", "sinhvien") ||
                    ContainsAny(message, "sinh vien", "sinhvien"),

                ForSchool = conversationProfile.ForSchool || ContainsAny(message, "di hoc", "den truong", "hoc hang ngay"),
                ForWork = conversationProfile.ForWork || ContainsAny(message, "di lam", "cong so", "di lam hang ngay"),
                ForCity = conversationProfile.ForCity || ContainsAny(message, "di pho", "trong pho", "do thi", "hang ngay"),
                ForTour = conversationProfile.ForTour || ContainsAny(message, "di tour", "duong dai", "di xa", "phuot"),
                ExplicitlyWants50cc = explicitlyWants50cc,
                WantsEasyControl = wantsEasyControl,
                WantsFuelSaving = conversationProfile.WantsFuelSaving || ContainsAny(message, "tiet kiem xang", "it ton xang"),
                WantsLargeStorage = conversationProfile.WantsLargeStorage || ContainsAny(message, "cop rong", "de do", "chua do"),

                WantsScooter = !dislikesScooter &&
    (explicitlyWantsScooter || (!string.IsNullOrWhiteSpace(preferredCategory) && preferredCategory.Contains("ga"))),

                WantsUnderbone = !dislikesUnderbone &&
    (explicitlyWantsUnderbone || (!string.IsNullOrWhiteSpace(preferredCategory) && preferredCategory.Contains("so"))),

                WantsManual = !dislikesManual &&
    (explicitlyWantsManual || (!string.IsNullOrWhiteSpace(preferredCategory) && preferredCategory.Contains("con"))),

                DislikesManual = dislikesManual,
                DislikesScooter = dislikesScooter,
                DislikesUnderbone = dislikesUnderbone,

                PrefersMaleStyle =
                    conversationProfile.PrefersMaleStyle ||
                    ContainsAny(target, "nam") ||
                    ContainsAny(message, "cho nam", "nam di lam", "nam di pho"),

                PrefersFemaleStyle =
                    conversationProfile.PrefersFemaleStyle ||
                    ContainsAny(target, "nu") ||
                    ContainsAny(message, "cho nu", "nu di lam", "nu di pho", "cho phai nu"),

                IsOpenConsultation = true,

                HeightCm = conversationProfile.HeightCm ?? ExtractHeightCm(message),
                NeedsCompactFit = needsCompactFit,
                NeedsLowSeat = needsLowSeat,
                RequestedStyles = requestedStyles
            };
        }
        private static int ScoreByEngineCc(ProductContext product, RequestProfile profile)
        {
            var score = 0;

            bool is50cc =
                product.EngineCc.HasValue && product.EngineCc.Value <= 50;

            if (!is50cc)
                return 0;

            if (profile.ExplicitlyWants50cc)
            {
                score += 18;
                return score;
            }

            if (profile.IsStudent && !profile.ForWork)
            {
                score += 4;
                return score;
            }

            score -= 22;
            return score;
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

            var inferredTags = InferTags(name, brand, category, vehicleType, product.CC);
            var dbTags = ParseTags(product.Tags);

            foreach (var tag in dbTags)
            {
                inferredTags.Add(tag);
            }

            return new ProductContext
            {
                Name = name,
                Brand = brand,
                Category = category,
                VehicleType = vehicleType,
                Tags = inferredTags,
                SeatHeightMm = InferSeatHeightMm(name, category, vehicleType, product.CC, inferredTags),
                Price = product.Gia,
                Stock = product.SoLuong,
                EngineCc = product.CC
            };
        }

        private static HashSet<string> InferTags(
            string name,
            string brand,
            string category,
            VehicleType vehicleType,
            short? cc)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (vehicleType == VehicleType.Scooter)
            {
                tags.Add("di pho");
                tags.Add("linh hoat");
                tags.Add("de dieu khien");
                tags.Add("de di");
            }

            if (vehicleType == VehicleType.Underbone)
            {
                tags.Add("tiet kiem");
                tags.Add("di hoc");
                tags.Add("ben");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
            }

            if (vehicleType == VehicleType.Manual)
            {
                tags.Add("the thao");
                tags.Add("di tour");
                tags.Add("manh me");
                tags.Add("ham ho");
            }

            if (ContainsAny(name, "vision"))
            {
                tags.Add("nu tinh");
                tags.Add("trung tinh");
                tags.Add("de di");
                tags.Add("nho gon");
                tags.Add("thuc dung");
                tags.Add("yen thap");
                tags.Add("de chong chan");
            }

            if (ContainsAny(name, "zip"))
            {
                tags.Add("nu tinh");
                tags.Add("nho gon");
                tags.Add("yen thap");
                tags.Add("de chong chan");
                tags.Add("di pho");
                tags.Add("de di");
                tags.Add("thanh lich");
            }
            if (ContainsAny(name, "attila", "venus"))
            {
                tags.Add("nu tinh");
                tags.Add("thanh lich");
                tags.Add("de di");
                tags.Add("di pho");
                tags.Add("trung tinh");
                tags.Add("gon");
            }

            if (ContainsAny(name, "shark mini", "shark"))
            {
                tags.Add("nu tinh");
                tags.Add("de di");
                tags.Add("di pho");
                tags.Add("trung tinh");
                tags.Add("gon");
            }
            if (ContainsAny(name, "latte"))
            {
                tags.Add("nu tinh");
                tags.Add("thanh lich");
                tags.Add("de di");
                tags.Add("nhe nhang");
                tags.Add("trung tinh");
                tags.Add("gon");
            }

            if (ContainsAny(name, "grande", "janus"))
            {
                tags.Add("nu tinh");
                tags.Add("thanh lich");
                tags.Add("nhe nhang");
                tags.Add("de di");
                tags.Add("gon");
            }

            if (ContainsAny(name, "lead", "freego"))
            {
                tags.Add("cop rong");
                tags.Add("de do");
                tags.Add("thuc dung");
                tags.Add("di pho");
                tags.Add("trung tinh");
                tags.Add("di lam");
                tags.Add("linh hoat");
                tags.Add("chua do");
            }

            if (ContainsAny(name, "air blade", "vario"))
            {
                tags.Add("the thao");
                tags.Add("ca tinh");
                tags.Add("nam tinh");
                tags.Add("trung tinh");
                tags.Add("dam chac");
                tags.Add("di lam");
            }

            if (ContainsAny(name, "wave", "sirius", "future", "jupiter"))
            {
                tags.Add("tiet kiem");
                tags.Add("di hoc");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
                tags.Add("ben");
                tags.Add("di lam");

                if (ContainsAny(name, "future", "jupiter"))
                {
                    tags.Add("dam chac");
                }
            }

            if (ContainsAny(name, "winner", "exciter"))
            {
                tags.Add("the thao");
                tags.Add("manh me");
                tags.Add("nam tinh");
                tags.Add("ham ho");
            }

            if (ContainsAny(name, "pcx", "sh", "beverly"))
            {
                tags.Add("dam chac");
                tags.Add("to lon");
                tags.Add("sang");
            }

            if (ContainsAny(name, "address"))
            {
                tags.Add("di pho");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
                tags.Add("de di");
            }

            if (ContainsAny(name, "impulse"))
            {
                tags.Add("di pho");
                tags.Add("thuc dung");
                tags.Add("trung tinh");
                tags.Add("ca tinh");
            }

            if (ContainsAny(brand, "yamaha"))
            {
                tags.Add("nang dong");
            }

            if (ContainsAny(category, "ga"))
            {
                tags.Add("de dieu khien");
                tags.Add("xe ga");
            }

            if (ContainsAny(category, "so"))
            {
                tags.Add("xe so");
            }

            if (ContainsAny(category, "con"))
            {
                tags.Add("con tay");
            }

            if (cc.HasValue)
            {
                if (cc.Value <= 125)
                {
                    tags.Add("de di");
                    tags.Add("di pho");
                }

                if (cc.Value >= 150)
                {
                    tags.Add("manh me");
                    tags.Add("dam chac");
                }
            }

            return tags;
        }

        private static decimal? InferSeatHeightMm(
            string name,
            string category,
            VehicleType vehicleType,
            short? cc,
            HashSet<string> tags)
        {
            if (tags.Contains("yen thap") || tags.Contains("de chong chan"))
            {
                return 760m;
            }

            if (ContainsAny(name, "zip", "vision", "latte", "janus"))
            {
                return 760m;
            }

            if (ContainsAny(name, "lead", "freego", "grande"))
            {
                return 770m;
            }

            if (ContainsAny(name, "air blade", "vario"))
            {
                return 780m;
            }

            if (vehicleType == VehicleType.Underbone)
            {
                return 770m;
            }

            if (vehicleType == VehicleType.Manual)
            {
                return 790m;
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
            if (ContainsAny(category, "con", "con tay", "côn", "côn tay") ||
                ContainsAny(name, "exciter", "winner", "husky"))
            {
                return VehicleType.Manual;
            }

            if (ContainsAny(category, "so", "xe so", "số", "xe số") ||
                ContainsAny(name, "wave", "sirius", "jupiter", "future", "galaxy"))
            {
                return VehicleType.Underbone;
            }

            if (ContainsAny(category, "ga", "tay ga", "xe ga") ||
                ContainsAny(name, "vision", "janus", "latte", "lead", "zip", "freego", "grande", "air blade", "vario", "address", "impulse"))
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

            if (ContainsAny(message, "the thao", "nang dong", "ca tinh", "tre trung"))
            {
                styles.Add(StyleTag.Sporty);
            }

            if (ContainsAny(message, "thanh lich", "nha nhan", "nhe nhang", "sang", "mem mai", "nu tinh"))
            {
                styles.Add(StyleTag.Elegant);
            }

            if (ContainsAny(message, "ham ho", "manh me", "dam", "chat"))
            {
                styles.Add(StyleTag.Aggressive);
            }

            if (ContainsAny(message,
                "nho gon", "gon", "linh hoat", "nguoi nho", "nho con", "dang nguoi nho",
                "de xoay tro", "gon nhe", "xe nho"))
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
                .Replace("_", " ")
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

        private static HashSet<string> ParseTags(string? tags)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(tags))
            {
                return result;
            }

            var parts = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                var normalized = Normalize(part);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
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
            public string? PreferredBrand { get; set; }
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
            public bool NeedsCompactFit { get; set; }
            public bool NeedsLowSeat { get; set; }
            public bool ExplicitlyWants50cc { get; set; }
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