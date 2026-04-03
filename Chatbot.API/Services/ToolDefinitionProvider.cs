using System.Text.Json.Nodes;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        public JsonArray GetTools()
        {
            return new JsonArray
    {
        BuildSearchProductsTool(),
        BuildLookupOrderTool(),
        BuildGetProductDetailTool(),
        BuildGetProductsByBrandTool(),
        BuildGetProductsByPriceRangeTool(),
        BuildGetProductsByBrandAndPriceTool(),
        BuildGetProductsByFiltersTool()
    };
        }

        private static JsonObject BuildSearchProductsTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolNames.SearchProducts,
                    ["description"] = "Tìm kiếm sản phẩm xe máy theo tên mẫu xe hoặc từ khóa cụ thể, ví dụ: Honda Vision, Air Blade, Yamaha Sirius.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["keyword"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Từ khóa tìm kiếm sản phẩm."
                            }
                        },
                        ["required"] = new JsonArray("keyword"),
                        ["additionalProperties"] = false
                    }
                }
            };
        }

        private static JsonObject BuildLookupOrderTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolNames.LookupOrder,
                    ["description"] = "Tra cứu trạng thái đơn hàng bằng mã đơn hàng và số điện thoại người đặt.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["orderId"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Mã đơn hàng cần tra cứu."
                            },
                            ["phone"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Số điện thoại của người đặt hàng."
                            }
                        },
                        ["required"] = new JsonArray("orderId", "phone"),
                        ["additionalProperties"] = false
                    }
                }
            };
        }

        private static JsonObject BuildGetProductDetailTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolNames.GetProductDetail,
                    ["description"] = "Lấy thông tin chi tiết của một sản phẩm xe máy theo mã sản phẩm.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["productId"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Mã sản phẩm cần xem chi tiết."
                            }
                        },
                        ["required"] = new JsonArray("productId"),
                        ["additionalProperties"] = false
                    }
                }
            };
        }

        private static JsonObject BuildGetProductsByBrandTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolNames.GetProductsByBrand,
                    ["description"] = "Lấy danh sách xe máy theo hãng, dùng khi người dùng hỏi các mẫu xe của Honda, Yamaha, Suzuki, Piaggio hoặc SYM.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["brand"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Tên hãng xe cần tìm."
                            }
                        },
                        ["required"] = new JsonArray("brand"),
                        ["additionalProperties"] = false
                    }
                }
            };
        }

        private static JsonObject BuildGetProductsByPriceRangeTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolNames.GetProductsByPriceRange,
                    ["description"] = "Lấy danh sách xe máy theo khoảng giá đơn thuần. Dùng khi người dùng chỉ hỏi xe dưới một mức giá, trên một mức giá hoặc trong một khoảng giá. Nếu người dùng còn nhắc thêm hãng xe hoặc loại xe như xe ga, xe số thì ưu tiên dùng tool chuyên biệt hơn.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["minPrice"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Giá nhỏ nhất. Có thể bỏ trống."
                            },
                            ["maxPrice"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Giá lớn nhất. Có thể bỏ trống."
                            }
                        },
                        ["required"] = new JsonArray(),
                        ["additionalProperties"] = false
                    }
                }
            };
        }
        private static JsonObject BuildGetProductsByBrandAndPriceTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_products_by_brand_and_price",
                    ["description"] = "Lấy danh sách xe máy theo hãng, mức giá tối đa và có thể kèm loại xe. Dùng khi người dùng hỏi như: xe Honda dưới 40 triệu, Yamaha tầm 30 triệu, xe ga Yamaha dưới 40 triệu, nữ đi học nên mua xe ga hãng Honda khoảng 45 triệu.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["brand"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Tên hãng xe cần tìm, ví dụ: Honda, Yamaha, Suzuki, Piaggio, SYM."
                            },
                            ["maxPrice"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Mức giá tối đa theo đơn vị VNĐ, ví dụ: 45000000."
                            },
                            ["category"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Loại xe nếu người dùng có nhắc tới, ví dụ: xe ga, xe số, côn tay. Nếu không có thì có thể bỏ trống."
                            },
                            ["take"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Số lượng sản phẩm tối đa cần lấy, ví dụ: 5 hoặc 10. Nếu không có thì dùng mặc định."
                            }
                        },
                        ["required"] = new JsonArray("brand", "maxPrice"),
                        ["additionalProperties"] = false
                    }
                }
            };
        }
        private static JsonObject BuildGetProductsByFiltersTool()
        {
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_products_by_filters",
                    ["description"] = "Lấy danh sách xe máy theo nhiều điều kiện kết hợp như hãng, khoảng giá và loại xe. Dùng khi người dùng hỏi phức hợp như: xe ga Honda dưới 50 triệu, Yamaha từ 30 đến 40 triệu, xe số cho sinh viên giá rẻ, hoặc cần lọc đồng thời nhiều tiêu chí.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["brand"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Tên hãng xe, ví dụ Honda, Yamaha."
                            },
                            ["minPrice"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Giá nhỏ nhất, có thể bỏ trống."
                            },
                            ["maxPrice"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["description"] = "Giá lớn nhất, có thể bỏ trống."
                            },
                            ["category"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "Loại xe, ví dụ: xe ga, tay ga, scooter, xe số, côn tay."
                            },
                            ["take"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "Số lượng sản phẩm tối đa."
                            }
                        },
                        ["required"] = new JsonArray(),
                        ["additionalProperties"] = false
                    }
                }
            };
        }
    }
}