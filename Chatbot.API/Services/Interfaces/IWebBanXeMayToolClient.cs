using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IWebBanXeMayToolClient
    {
        Task<ProductSearchResponseDto?> SearchProductsAsync(string keyword, int take = 5);
        Task<ProductDetailDto?> GetProductDetailAsync(int id);
        Task<OrderStatusDto?> LookupOrderAsync(int maDH, string phone);
        Task<ProductSearchResponseDto?> GetProductsByBrandAsync(string brand, int take = 10);
        Task<ProductSearchResponseDto?> GetProductsByPriceRangeAsync(decimal? minPrice, decimal? maxPrice, int take = 10);
        Task<ProductSearchResponseDto?> GetProductsByBrandAndPriceAsync(string brand, decimal maxPrice, string? category = null, int take = 10);
        Task<ProductSearchResponseDto?> GetProductsByFiltersAsync(
    string? brand,
    decimal? minPrice,
    decimal? maxPrice,
    string? category,
    int take = 10);
    }
}
