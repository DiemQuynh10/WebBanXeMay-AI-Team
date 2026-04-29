using System.Diagnostics;
using System.Text.Json;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ToolDispatcher : IToolDispatcher
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly ILogger<ToolDispatcher> _logger;

        public ToolDispatcher(
            IWebBanXeMayToolClient toolClient,
            ILogger<ToolDispatcher> logger)
        {
            _toolClient = toolClient;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(string functionName, string argumentsJson)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                var root = doc.RootElement;

                _logger.LogInformation(
                    "Tool execution started. FunctionName: {FunctionName}, Arguments: {Arguments}",
                    functionName,
                    argumentsJson);

                string resultJson = functionName switch
                {
                    ToolNames.SearchProducts => await HandleSearchProductsAsync(root, functionName),
                    ToolNames.LookupOrder => await HandleLookupOrderAsync(root, functionName),
                    ToolNames.GetProductDetail => await HandleGetProductDetailAsync(root, functionName),
                    ToolNames.GetProductsByBrand => await HandleGetProductsByBrandAsync(root, functionName),
                    ToolNames.GetProductsByPriceRange => await HandleGetProductsByPriceRangeAsync(root, functionName),
                    ToolNames.GetProductsByBrandAndPrice => await HandleGetProductsByBrandAndPriceAsync(root, functionName),
                    ToolNames.GetProductsByFilters => await HandleGetProductsByFiltersAsync(root, functionName),
                    _ => BuildError(functionName, $"Tool '{functionName}' chưa được hỗ trợ.")
                };

                stopwatch.Stop();

                _logger.LogInformation(
                    "Tool execution finished. FunctionName: {FunctionName}, ElapsedMs: {ElapsedMs}",
                    functionName,
                    stopwatch.ElapsedMilliseconds);

                return resultJson;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Tool execution failed. FunctionName: {FunctionName}, Arguments: {Arguments}, ElapsedMs: {ElapsedMs}",
                    functionName,
                    argumentsJson,
                    stopwatch.ElapsedMilliseconds);

                return JsonSerializer.Serialize(new
                {
                    success = false,
                    tool = functionName,
                    message = "Có lỗi xảy ra khi thực thi tool.",
                    error = ex.Message,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    data = (object?)null
                });
            }
        }

        private async Task<string> HandleSearchProductsAsync(JsonElement root, string functionName)
        {
            var keyword = GetString(root, "keyword");

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BuildError(functionName, "Thiếu từ khóa tìm kiếm sản phẩm.");
            }

            var result = await _toolClient.SearchProductsAsync(keyword, 10);
            var hasData = result != null && result.Items != null && result.Items.Any();

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData ? "Tìm sản phẩm thành công." : "Không tìm thấy sản phẩm phù hợp.");
        }

        private async Task<string> HandleLookupOrderAsync(JsonElement root, string functionName)
        {
            var orderId = GetInt(root, "orderId");
            var phone = GetString(root, "phone");
            var userId = GetString(root, "userId");

            if (orderId <= 0)
            {
                return BuildError(functionName, "Mã đơn hàng không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                return BuildError(functionName, "Thiếu số điện thoại để tra cứu đơn hàng.");
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return BuildError(functionName, "Để bảo mật thông tin đơn hàng, bạn vui lòng đăng nhập trước khi tra cứu đơn nhé.");
            }

            var result = await _toolClient.LookupOrderAsync(orderId, phone, userId);
            var hasData = result != null;

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData
                    ? "Tra cứu đơn hàng thành công."
                    : "Không tìm thấy đơn hàng phù hợp.");
        }
        private async Task<string> HandleGetProductDetailAsync(JsonElement root, string functionName)
        {
            var productId = GetInt(root, "productId");

            if (productId <= 0)
            {
                return BuildError(functionName, "Mã sản phẩm không hợp lệ.");
            }

            var result = await _toolClient.GetProductDetailAsync(productId);
            var hasData = result != null;

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData
                    ? "Lấy chi tiết sản phẩm thành công."
                    : "Không tìm thấy chi tiết sản phẩm.");
        }

        private async Task<string> HandleGetProductsByBrandAsync(JsonElement root, string functionName)
        {
            var brand = GetString(root, "brand");

            if (string.IsNullOrWhiteSpace(brand))
            {
                return BuildError(functionName, "Thiếu tên hãng xe.");
            }

            var result = await _toolClient.GetProductsByBrandAsync(brand);
            var hasData = result != null && result.Items != null && result.Items.Any();

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData
                    ? $"Lấy danh sách sản phẩm theo hãng {brand} thành công."
                    : $"Không tìm thấy sản phẩm nào của hãng {brand}.");
        }

        private async Task<string> HandleGetProductsByPriceRangeAsync(JsonElement root, string functionName)
        {
            var minPrice = GetNullableDecimal(root, "minPrice");
            var maxPrice = GetNullableDecimal(root, "maxPrice");

            if (minPrice == null && maxPrice == null)
            {
                return BuildError(functionName, "Cần cung cấp ít nhất một giá trị minPrice hoặc maxPrice.");
            }

            if (minPrice != null && maxPrice != null && minPrice > maxPrice)
            {
                return BuildError(functionName, "Khoảng giá không hợp lệ: minPrice không được lớn hơn maxPrice.");
            }

            var result = await _toolClient.GetProductsByPriceRangeAsync(minPrice, maxPrice);
            var hasData = result != null && result.Items != null && result.Items.Any();

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData
                    ? "Lấy danh sách sản phẩm theo khoảng giá thành công."
                    : "Không tìm thấy sản phẩm phù hợp với khoảng giá.");
        }
        private async Task<string> HandleGetProductsByBrandAndPriceAsync(JsonElement root, string functionName)
        {
            var brand = GetString(root, "brand");
            var category = GetString(root, "category");
            var maxPrice = GetNullableDecimal(root, "maxPrice");
            var take = GetInt(root, "take");

            if (take <= 0) take = 10;

            if (string.IsNullOrWhiteSpace(brand))
            {
                return BuildError(functionName, "Thiếu tên hãng xe.");
            }

            if (maxPrice == null || maxPrice <= 0)
            {
                return BuildError(functionName, "Giá tối đa không hợp lệ.");
            }

            var result = await _toolClient.GetProductsByBrandAndPriceAsync(
                brand,
                maxPrice.Value,
                string.IsNullOrWhiteSpace(category) ? null : category,
                take);

            var hasData = result != null && result.Items != null && result.Items.Any();

            var message = hasData
                ? string.IsNullOrWhiteSpace(category)
                    ? $"Lấy danh sách xe {brand} dưới {maxPrice.Value:N0} VNĐ thành công."
                    : $"Lấy danh sách xe {category} hãng {brand} dưới {maxPrice.Value:N0} VNĐ thành công."
                : string.IsNullOrWhiteSpace(category)
                    ? $"Không tìm thấy xe {brand} phù hợp với mức giá."
                    : $"Không tìm thấy xe {category} hãng {brand} phù hợp với mức giá.";

            return BuildSuccess(functionName, hasData, result, message);
        }
        private static string BuildSuccess(string toolName, bool success, object? data, string message)
        {
            return JsonSerializer.Serialize(new
            {
                success,
                tool = toolName,
                message,
                data
            });
        }

        private static string BuildError(string toolName, string message)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                tool = toolName,
                message,
                data = (object?)null
            });
        }

        private static string GetString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return (prop.GetString() ?? string.Empty).Trim();
            }

            return string.Empty;
        }

        private static int GetInt(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
                {
                    return value;
                }

                if (prop.ValueKind == JsonValueKind.String &&
                    int.TryParse((prop.GetString() ?? string.Empty).Trim(), out var parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }

        private static decimal? GetNullableDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse((prop.GetString() ?? string.Empty).Trim(), out var parsed))
            {
                return parsed;
            }

            return null;
        }
        private async Task<string> HandleGetProductsByFiltersAsync(JsonElement root, string functionName)
        {
            var brand = GetString(root, "brand");
            var category = GetString(root, "category");
            var minPrice = GetNullableDecimal(root, "minPrice");
            var maxPrice = GetNullableDecimal(root, "maxPrice");
            var take = GetInt(root, "take");
            if (take <= 0) take = 10;

            if (minPrice != null && maxPrice != null && minPrice > maxPrice)
            {
                return BuildError(functionName, "Khoảng giá không hợp lệ: minPrice không được lớn hơn maxPrice.");
            }

            var result = await _toolClient.GetProductsByFiltersAsync(
                string.IsNullOrWhiteSpace(brand) ? null : brand,
                minPrice,
                maxPrice,
                string.IsNullOrWhiteSpace(category) ? null : category,
                take);

            var hasData = result != null && result.Items != null && result.Items.Any();

            return BuildSuccess(
                functionName,
                hasData,
                result,
                hasData
                    ? "Lấy danh sách sản phẩm theo bộ lọc thành công."
                    : "Không tìm thấy sản phẩm phù hợp với bộ lọc.");
        }
    }
}