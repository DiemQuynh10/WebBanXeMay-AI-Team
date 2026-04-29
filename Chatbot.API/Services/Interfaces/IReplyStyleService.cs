using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IReplyStyleService
    {
        string BuildRecommendationReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            Func<ProductSummaryDto, string> reasonFactory);

        string BuildRecommendationNoMatchReply(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? effectiveCategory);

        string BuildRefinementReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            string normalizedMessage,
            Func<ProductSummaryDto, string> reasonFactory);

        string BuildRefinementNoMatchReply(ParsedIntent intent);

        string BuildRefinementBrandRelaxedReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent);

        string BuildSearchReply(
            List<ProductSummaryDto> items,
            ParsedIntent intent,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            Func<ProductSummaryDto, string> reasonFactory);

        string BuildSearchEmptyReply(
            ParsedIntent intent,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            List<ProductSummaryDto> nearMatches);
        string BuildClusteredRecommendationReply(
    IReadOnlyList<ProductSummaryDto> ranked,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    ProductSummaryDto? anchor,
    List<string> bucketNarratives,
    string normalizedMessage,
    Func<ProductSummaryDto, List<string>> getReasons);
    }
}