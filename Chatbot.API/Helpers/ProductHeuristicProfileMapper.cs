using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Helpers
{
    public static class ProductHeuristicProfileMapper
    {
        public static ProductHeuristicProfile Map(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            var category = (product.Loai ?? string.Empty).ToLowerInvariant();

            var profile = new ProductHeuristicProfile();

            // Nhóm hợp nữ / dễ đi / dáng mềm
            if (name.Contains("vision") ||
                name.Contains("latte") ||
                name.Contains("grande") ||
                name.Contains("attila") ||
                name.Contains("zip"))
            {
                profile.FemaleFit = true;
                profile.EasyControl = true;
                profile.LowSeatLike = true;
                profile.CityFit = true;
            }

            // Nhóm cốp rộng / thực dụng / đi làm
            if (name.Contains("freego") ||
                name.Contains("lead") ||
                name.Contains("latte") ||
                name.Contains("address"))
            {
                profile.LargeStorageLike = true;
                profile.WorkFit = true;
                profile.PracticalStyle = true;
            }

            // Nhóm tiết kiệm xăng / đi học / thực dụng
            if (name.Contains("wave") ||
                name.Contains("future") ||
                name.Contains("sirius") ||
                name.Contains("vision"))
            {
                profile.FuelSavingLike = true;
                profile.SchoolFit = true;
                profile.PracticalStyle = true;
            }

            // Nhóm đi làm / cân bằng / đi phố
            if (name.Contains("air blade") ||
                name.Contains("freego") ||
                name.Contains("future"))
            {
                profile.WorkFit = true;
                profile.CityFit = true;
            }

            // Thanh lịch
            if (name.Contains("latte") ||
                name.Contains("grande") ||
                name.Contains("attila"))
            {
                profile.ElegantStyle = true;
            }

            // Thể thao
            if (name.Contains("winner") ||
                name.Contains("exciter") ||
                name.Contains("air blade"))
            {
                profile.SportyStyle = true;
                profile.MaleFit = true;
            }

            // Theo loại xe
            if (category.Contains("xe ga"))
            {
                profile.CityFit = true;
                profile.EasyControl = true;
            }

            if (category.Contains("xe số") || category.Contains("xe so"))
            {
                profile.FuelSavingLike = true;
                profile.SchoolFit = true;
                profile.PracticalStyle = true;
            }

            if (category.Contains("côn tay") || category.Contains("con tay"))
            {
                profile.SportyStyle = true;
                profile.MaleFit = true;
            }

            return profile;
        }
    }
}