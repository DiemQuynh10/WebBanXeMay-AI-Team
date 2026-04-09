using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Helpers
{
    public static class ChatProductCardMapper
    {
        public static ChatProductCard Map(ProductSummaryDto product)
        {
            return new ChatProductCard
            {
                Id = product.Id,
                Ten = product.Ten,
                Slug = product.Slug,
                Gia = product.Gia,
                SoLuong = product.SoLuong,
                CC = product.CC?.ToString(),
                ImageUrl = product.ImageUrl,
                ThuongHieu = product.ThuongHieu,
                Loai = product.Loai
            };
        }

        public static List<ChatProductCard> MapMany(IEnumerable<ProductSummaryDto> products, int take = 4)
        {
            return products
                .Where(x => x != null)
                .Take(take)
                .Select(Map)
                .ToList();
        }
    }
}