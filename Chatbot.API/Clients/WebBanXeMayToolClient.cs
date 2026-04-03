using System.Net.Http.Headers;
using System.Text.Json;
using Chatbot.API.Configurations;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Clients
{
    public class WebBanXeMayToolClient : IWebBanXeMayToolClient
    {
        private readonly HttpClient _httpClient;
        private readonly ToolApiOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<WebBanXeMayToolClient> _logger;

        public WebBanXeMayToolClient(
            HttpClient httpClient,
            IOptions<ToolApiOptions> options,
            ILogger<WebBanXeMayToolClient> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            if (!_httpClient.DefaultRequestHeaders.Contains("X-Tool-Api-Key"))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Tool-Api-Key", _options.ApiKey);
            }

            if (!_httpClient.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            {
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        public async Task<ProductDetailDto?> GetProductDetailAsync(int id)
        {
            var url = $"{_options.BaseUrl}/api/tools/products/{id}";
            return await GetAsync<ProductDetailDto>(url, nameof(GetProductDetailAsync));
        }

        public async Task<OrderStatusDto?> LookupOrderAsync(int maDH, string phone)
        {
            phone = phone?.Trim() ?? string.Empty;

            var url = $"{_options.BaseUrl}/api/tools/orders/lookup?maDH={maDH}&phone={Uri.EscapeDataString(phone)}";
            return await GetAsync<OrderStatusDto>(url, nameof(LookupOrderAsync));
        }

        public async Task<ProductSearchResponseDto?> SearchProductsAsync(string keyword, int take = 5)
        {
            keyword = keyword?.Trim() ?? string.Empty;

            var url = $"{_options.BaseUrl}/api/tools/products/search?keyword={Uri.EscapeDataString(keyword)}&take={take}";
            return await GetAsync<ProductSearchResponseDto>(url, nameof(SearchProductsAsync));
        }

        public async Task<ProductSearchResponseDto?> GetProductsByBrandAsync(string brand, int take = 10)
        {
            brand = brand?.Trim() ?? string.Empty;
            return await SearchProductsAsync(brand, take);
        }

        public async Task<ProductSearchResponseDto?> GetProductsByPriceRangeAsync(decimal? minPrice, decimal? maxPrice, int take = 10)
        {
            var query = new List<string>();

            if (minPrice.HasValue)
                query.Add($"minPrice={minPrice.Value}");

            if (maxPrice.HasValue)
                query.Add($"maxPrice={maxPrice.Value}");

            query.Add($"take={take}");

            var url = $"{_options.BaseUrl}/api/tools/products/by-price-range?{string.Join("&", query)}";
            return await GetAsync<ProductSearchResponseDto>(url, nameof(GetProductsByPriceRangeAsync));
        }

        private async Task<T?> GetAsync<T>(string url, string operationName)
        {
            try
            {
                _logger.LogInformation(
                    "Calling Tool API. Operation: {OperationName}, Url: {Url}",
                    operationName,
                    url);

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Tool API returned non-success status. Operation: {OperationName}, StatusCode: {StatusCode}, Body: {Body}",
                        operationName,
                        (int)response.StatusCode,
                        responseBody);

                    return default;
                }

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    _logger.LogWarning(
                        "Tool API returned empty response body. Operation: {OperationName}, Url: {Url}",
                        operationName,
                        url);

                    return default;
                }

                var result = JsonSerializer.Deserialize<T>(responseBody, _jsonOptions);

                if (result == null)
                {
                    _logger.LogWarning(
                        "Tool API response could not be deserialized. Operation: {OperationName}, Url: {Url}",
                        operationName,
                        url);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while calling Tool API. Operation: {OperationName}, Url: {Url}",
                    operationName,
                    url);

                return default;
            }
        }
        public async Task<ProductSearchResponseDto?> GetProductsByBrandAndPriceAsync(
    string brand,
    decimal maxPrice,
    string? category = null,
    int take = 10)
        {
            brand = brand?.Trim() ?? string.Empty;

            var query = new List<string>
    {
        $"brand={Uri.EscapeDataString(brand)}",
        $"maxPrice={maxPrice}",
        $"take={take}"
    };

            if (!string.IsNullOrWhiteSpace(category))
            {
                query.Add($"category={Uri.EscapeDataString(category)}");
            }

            var url = $"{_options.BaseUrl}/api/tools/products/by-brand-and-price?{string.Join("&", query)}";

            _logger.LogInformation(
                "Calling Tool API. Operation: GetProductsByBrandAndPriceAsync, Url: {Url}",
                url);

            return await GetAsync<ProductSearchResponseDto>(url, nameof(GetProductsByBrandAndPriceAsync));
        }
        public async Task<ProductSearchResponseDto?> GetProductsByFiltersAsync(
    string? brand,
    decimal? minPrice,
    decimal? maxPrice,
    string? category,
    int take = 10)
        {
            var query = new List<string>();

            if (!string.IsNullOrWhiteSpace(brand))
                query.Add($"brand={Uri.EscapeDataString(brand)}");

            if (minPrice.HasValue)
                query.Add($"minPrice={minPrice.Value}");

            if (maxPrice.HasValue)
                query.Add($"maxPrice={maxPrice.Value}");

            if (!string.IsNullOrWhiteSpace(category))
                query.Add($"category={Uri.EscapeDataString(category)}");

            query.Add($"take={take}");

            var url = $"{_options.BaseUrl}/api/tools/products/by-filters?{string.Join("&", query)}";

            _logger.LogInformation("Calling Tool API. Operation: GetProductsByFiltersAsync, Url: {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<ProductSearchResponseDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }
}