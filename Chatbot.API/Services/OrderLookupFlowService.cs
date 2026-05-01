using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;


namespace Chatbot.API.Services
{
    public class OrderLookupFlowService : IOrderLookupFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<OrderLookupFlowService> _logger;

        public OrderLookupFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<OrderLookupFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }

        public async Task<ChatResponse> HandleAsync(ChatRequest request, string normalizedMessage)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString()
                : request.ConversationId.Trim();

            request.ConversationId = conversationId;

            var isAuthenticated = request.IsAuthenticated;
            var currentUserId = request.UserId?.Trim();

            if (!isAuthenticated || string.IsNullOrWhiteSpace(currentUserId))
            {
                await _conversationPreferenceService.ClearOrderLookupPendingAsync(conversationId);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = "Để bảo mật thông tin đơn hàng, bạn vui lòng đăng nhập tài khoản đã đặt hàng trước khi tra cứu đơn nhé."
                };
            }

            var profile = await _conversationPreferenceService.GetAsync(conversationId);

            var extractedOrderId = ExtractOrderId(normalizedMessage);
            var extractedPhone = !string.IsNullOrWhiteSpace(request.Phone)
                ? request.Phone.Trim()
                : ExtractPhone(normalizedMessage);

            var sameOrderReference = IsSameOrderReferenceFollowUp(normalizedMessage);
            var orderStatusFollowUp = IsOrderStatusFollowUp(normalizedMessage);
            var otherOrderReference = IsOtherOrderReferenceFollowUp(normalizedMessage);

            int? orderId = extractedOrderId;
            string? phone = extractedPhone;

            if (sameOrderReference || orderStatusFollowUp)
            {
                orderId ??= profile.LastResolvedOrderId ?? profile.PendingOrderId;
                phone ??= profile.LastResolvedOrderPhone ?? profile.PendingOrderPhone;
            }

            string? reusedPhoneNotice = null;

            if (extractedOrderId.HasValue)
            {
                var rememberedPhone = profile.LastResolvedOrderPhone ?? profile.PendingOrderPhone;

                if (!string.IsNullOrWhiteSpace(rememberedPhone))
                {
                    phone = rememberedPhone;

                    // 👉 Che bớt số điện thoại (ẩn cho đẹp + bảo mật)
                    var maskedPhone = rememberedPhone.Length >= 4
                        ? rememberedPhone.Substring(0, 4) + "xxxx"
                        : rememberedPhone;

                    reusedPhoneNotice =
                        $"Mình đang dùng số điện thoại {maskedPhone} từ lần tra trước để kiểm tra đơn DH{extractedOrderId.Value:D3}.\n" +
                        $"Nếu không đúng, bạn có thể gửi lại số điện thoại nhé.\n\n";
                }
            }
            // Nếu đang pending thì chỉ lấy Pending, KHÔNG tự lấy LastResolvedOrderId.
            // Đây là chỗ sửa quan trọng để tránh bot trả nhầm đơn cũ.
            if (profile.HasPendingOrderLookup)
            {
                orderId ??= profile.PendingOrderId;
                phone ??= profile.PendingOrderPhone;
            }

            _logger.LogInformation(
                "Order lookup flow started. ConversationId={ConversationId}, ExtractedOrderId={ExtractedOrderId}, ExtractedPhone={ExtractedPhone}, FinalOrderId={OrderId}, FinalPhone={Phone}, HasPending={HasPending}, SameOrderReference={SameOrderReference}, OtherOrderReference={OtherOrderReference}",
                conversationId,
                extractedOrderId,
                extractedPhone,
                orderId,
                phone,
                profile.HasPendingOrderLookup,
                sameOrderReference,
                otherOrderReference);

            if (otherOrderReference && !extractedOrderId.HasValue)
            {
                var rememberedPhone = profile.LastResolvedOrderPhone ?? profile.PendingOrderPhone;

                await SetPendingOrderLookupAsync(conversationId, null, rememberedPhone);

                if (!string.IsNullOrWhiteSpace(rememberedPhone))
                {
                    return new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = conversationId,
                        Reply = $"Mình đang nhớ số điện thoại {rememberedPhone} từ lần tra trước. Nếu bạn muốn xem đơn khác, bạn gửi thêm mã đơn hàng mới giúp mình nhé, ví dụ: DH002."
                    };
                }

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = "Nếu bạn muốn xem đơn khác, bạn gửi giúp mình mã đơn hàng mới nhé, ví dụ: DH002 hoặc mã đơn kèm số điện thoại đặt hàng."
                };
            }

            if (!orderId.HasValue && string.IsNullOrWhiteSpace(phone))
            {
                await SetPendingOrderLookupAsync(conversationId, null, null);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = "Để mình kiểm tra đơn hàng cho bạn, bạn vui lòng cung cấp mã đơn hàng và số điện thoại dùng khi đặt hàng nhé."
                };
            }

            if (!orderId.HasValue)
            {
                await SetPendingOrderLookupAsync(conversationId, null, phone);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = "Mình đã nhận được số điện thoại. Bạn vui lòng cung cấp thêm mã đơn hàng để mình tra cứu chính xác nhé."
                };
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                await SetPendingOrderLookupAsync(conversationId, orderId.Value, null);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = $"Mình đã nhận được mã đơn DH{orderId.Value:D3}. Bạn vui lòng gửi thêm số điện thoại dùng khi đặt hàng để mình kiểm tra nhé."
                };
            }

            if (!IsValidVietnamPhone(phone))
            {
                await SetPendingOrderLookupAsync(conversationId, orderId.Value, null);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    Reply = $"Mình đã nhận được mã đơn DH{orderId.Value:D3}, nhưng số điện thoại \"{phone}\" chưa hợp lệ. Bạn vui lòng gửi số điện thoại 10 chữ số dùng khi đặt hàng để mình kiểm tra nhé."
                };
            }

            var order = await _toolClient.LookupOrderAsync(orderId.Value, phone, currentUserId);

            if (order == null)
            {
                await SetPendingOrderLookupAsync(conversationId, orderId.Value, phone);

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    UsedTool = ToolNames.LookupOrder,
                    ConversationId = conversationId,
                    Reply = $"Mình chưa tìm thấy đơn hàng DH{orderId.Value:D3} với số điện thoại {phone}. Bạn vui lòng kiểm tra lại thông tin giúp mình nhé."
                };
            }

            var trangThaiDon = FormatOrderStatus(order.TrangThai);
            var trangThaiCoc = string.IsNullOrWhiteSpace(order.TrangThaiCoc)
                ? null
                : FormatDepositStatus(order.TrangThaiCoc);

            var itemText = order.Items != null && order.Items.Any()
                ? string.Join("; ", order.Items.Select(i => $"{i.TenSP} x {i.SoLuong}"))
                : "Không có chi tiết sản phẩm";

            var normalized = NormalizeText(normalizedMessage);

            if (orderStatusFollowUp)
            {
                if (normalized.Contains("giao"))
                {
                    if (trangThaiDon == "Đang giao")
                    {
                        return await SimpleReplyAsync(conversationId, orderId.Value, phone, $"Đơn {order.MaDH} đang được giao bạn nhé.");
                    }
                    else if (trangThaiDon == "Hoàn tất")
                    {
                        return await SimpleReplyAsync(conversationId, orderId.Value, phone,
     $"Đơn {order.MaDH} đã giao thành công rồi nhé.");
                    }
                    else if (trangThaiDon == "Đã hủy")
                    {
                        return await SimpleReplyAsync(conversationId, orderId.Value, phone,
    $"Đơn {order.MaDH} đã bị hủy nên không giao nữa nhé.");
                    }
                    else
                    {

                        return await SimpleReplyAsync(conversationId, orderId.Value, phone,
                            $"Hiện đơn {order.MaDH} đang ở trạng thái: {trangThaiDon}.");

                    }
                }

                if (normalized.Contains("coc"))
                {
                    return await SimpleReplyAsync(conversationId, orderId.Value, phone,
    $"Trạng thái cọc của đơn {order.MaDH}: {trangThaiCoc ?? "Chưa có thông tin"}.");
                }

                if (normalized.Contains("huy"))
                {
                    return await SimpleReplyAsync(conversationId, orderId.Value, phone,
     $"Trạng thái đơn {order.MaDH}: {trangThaiDon}.");
                }
            }

            var reply =
     $"Đơn hàng {order.MaDH} hiện ở trạng thái: {trangThaiDon}. " +
     $"Tổng tiền: {order.TongTien:N0} VNĐ. " +
     $"Người nhận: {order.NguoiNhan ?? "Chưa có thông tin"}. ";

            if (!string.IsNullOrWhiteSpace(trangThaiCoc))
            {
                reply += $"Trạng thái cọc: {trangThaiCoc}. ";
            }

            reply += $"Sản phẩm: {itemText}.";

            await _conversationPreferenceService.SaveResolvedOrderContextAsync(
                conversationId,
                orderId.Value,
                phone);

            await _conversationPreferenceService.ClearOrderLookupPendingAsync(conversationId);

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                UsedTool = ToolNames.LookupOrder,
                ConversationId = conversationId,
                Reply = (reusedPhoneNotice ?? "") + reply
            };
        }
        private async Task SetPendingOrderLookupAsync(string conversationId, int? orderId, string? phone)
        {
            var profile = await _conversationPreferenceService.GetAsync(conversationId);
            profile.HasPendingOrderLookup = true;
            profile.PendingOrderId = orderId;
            profile.PendingOrderPhone = phone;
            profile.ActiveFlow = ChatFlowType.OrderLookup;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }

        private static bool IsOrderStatusFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);

            return text.Contains("giao chua") ||
                   text.Contains("co giao chua") ||
                   text.Contains("dang giao chua") ||
                   text.Contains("giao den dau roi") ||
                   text.Contains("don den dau roi") ||
                   text.Contains("don toi dau roi") ||
                   text.Contains("trang thai sao") ||
                   text.Contains("tinh trang sao") ||
                   text.Contains("huy chua") ||
                   text.Contains("da huy chua") ||
                   text.Contains("hoan tat chua") ||
                   text.Contains("xong chua");
        }
        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim().ToLowerInvariant();

            text = text
                .Replace('à', 'a').Replace('á', 'a').Replace('ạ', 'a').Replace('ả', 'a').Replace('ã', 'a')
                .Replace('â', 'a').Replace('ầ', 'a').Replace('ấ', 'a').Replace('ậ', 'a').Replace('ẩ', 'a').Replace('ẫ', 'a')
                .Replace('ă', 'a').Replace('ằ', 'a').Replace('ắ', 'a').Replace('ặ', 'a').Replace('ẳ', 'a').Replace('ẵ', 'a')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẹ', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e')
                .Replace('ê', 'e').Replace('ề', 'e').Replace('ế', 'e').Replace('ệ', 'e').Replace('ể', 'e').Replace('ễ', 'e')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ị', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ọ', 'o').Replace('ỏ', 'o').Replace('õ', 'o')
                .Replace('ô', 'o').Replace('ồ', 'o').Replace('ố', 'o').Replace('ộ', 'o').Replace('ổ', 'o').Replace('ỗ', 'o')
                .Replace('ơ', 'o').Replace('ờ', 'o').Replace('ớ', 'o').Replace('ợ', 'o').Replace('ở', 'o').Replace('ỡ', 'o')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ụ', 'u').Replace('ủ', 'u').Replace('ũ', 'u')
                .Replace('ư', 'u').Replace('ừ', 'u').Replace('ứ', 'u').Replace('ự', 'u').Replace('ử', 'u').Replace('ữ', 'u')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỵ', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y')
                .Replace('đ', 'd');

            return text;
        }
        private static bool IsSameOrderReferenceFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return text.Contains("đơn đó") ||
                   text.Contains("don do") ||
                   text.Contains("đơn này") ||
                   text.Contains("don nay") ||
                   text.Contains("mã đó") ||
                   text.Contains("ma do") ||
                   text.Contains("mã này") ||
                   text.Contains("ma nay");
        }

        private static bool IsOtherOrderReferenceFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return text.Contains("đơn kia") ||
                   text.Contains("don kia") ||
                   text.Contains("mã kia") ||
                   text.Contains("ma kia") ||
                   text.Contains("đơn khác") ||
                   text.Contains("don khac") ||
                   text.Contains("mã khác") ||
                   text.Contains("ma khac");
        }
        private static int? ExtractOrderId(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var patterns = new[]
            {
        @"\bDH\s*[-#]?\s*0*(\d{1,9})\b",

        @"mã\s*đơn\s*(?:hàng)?\s*(?:số|so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})",
        @"ma\s*don\s*(?:hang)?\s*(?:so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})",

        @"đơn\s*hàng\s*(?:số|so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})",
        @"don\s*hang\s*(?:so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})",

        @"\bđơn\s*(?:số|so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})",
        @"\bdon\s*(?:so)?\s*[:#]?\s*(?:DH\s*[-#]?\s*)?0*(\d{1,9})"
    };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
                {
                    return id;
                }
            }

            return null;
        }

        private static string? ExtractPhone(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var match = Regex.Match(message, @"(?<!\d)0\d{8,9}(?!\d)");
            return match.Success ? match.Value : null;
        }

        private static bool IsValidVietnamPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return false;

            return Regex.IsMatch(phone, @"^0\d{9}$");
        }

        private static string FormatOrderStatus(string? status)
        {
            return status switch
            {
                "ChoXacNhan" => "Chờ xác nhận",
                "DangXuLy" => "Đang xử lý",
                "DangGiao" => "Đang giao",
                "HoanTat" => "Hoàn tất",
                "DaHuy" => "Đã hủy",
                null or "" => "Chưa có thông tin",
                _ => status
            };
        }
        private async Task<ChatResponse> SimpleReplyAsync(
      string conversationId,
      int orderId,
      string phone,
      string text)
        {
            await _conversationPreferenceService.SaveResolvedOrderContextAsync(
                conversationId,
                orderId,
                phone);

            await _conversationPreferenceService.ClearOrderLookupPendingAsync(conversationId);

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                UsedTool = ToolNames.LookupOrder,
                ConversationId = conversationId,
                Reply = text
            };
        }
        private static string FormatDepositStatus(string? status)
        {
            return status switch
            {
                "ChuaCoc" => "Chưa cọc",
                "ChoXacNhanCoc" => "Chờ xác nhận cọc",
                "DaCoc" => "Đã cọc",
                "HoanCoc" => "Hoàn cọc",
                "MatCoc" => "Mất cọc",
                null or "" => "Chưa có thông tin",
                _ => status
            };
        }
    }
}