using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Helpers
{
    public static class ProductPriceFilterHelper
    {
        public static List<ProductSummaryDto> ApplyStrictPriceFilter(
            List<ProductSummaryDto> items,
            ParsedIntent intent)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            IEnumerable<ProductSummaryDto> query = items;

            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
            {
                return query
                    .Where(x => x.Gia >= intent.PriceMin.Value && x.Gia <= intent.PriceMax.Value)
                    .ToList();
            }

            if (intent.FilterType == PriceFilterType.MaxOnly &&
                intent.PriceMax.HasValue)
            {
                return query
                    .Where(x => x.Gia <= intent.PriceMax.Value)
                    .ToList();
            }

            if (intent.FilterType == PriceFilterType.MinOnly &&
                intent.PriceMin.HasValue)
            {
                return query
                    .Where(x => x.Gia >= intent.PriceMin.Value)
                    .ToList();
            }

            if (intent.FilterType == PriceFilterType.Around &&
                intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = GetAroundDelta(target);

                return query
                    .Where(x => x.Gia >= target - delta && x.Gia <= target + delta)
                    .ToList();
            }

            return query.ToList();
        }

        public static decimal GetAroundDelta(decimal target)
        {
            if (target <= 20_000_000m) return 2_000_000m;
            if (target <= 35_000_000m) return 4_000_000m;
            if (target <= 50_000_000m) return 4_000_000m;
            return 5_000_000m;
        }
    }
}