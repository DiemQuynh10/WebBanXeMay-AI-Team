using Microsoft.AspNetCore.SignalR;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using Newtonsoft.Json; // Cần cài package: Install-Package Newtonsoft.Json

namespace WebBanXeMay.Hubs
{
    public class ChatHub : Hub
    {
        private readonly AppDbContext _context;

        public ChatHub(AppDbContext context)
        {
            _context = context;
        }

        public async Task SendText(string receiverId, string message)
        {
            var senderId = Context.UserIdentifier;

            var msg = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = message,
                IsProduct = false
            };

            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            // ❗ Chỉ gửi cho phía nhận (admin hoặc user), KHÔNG gửi lại cho chính người gửi
            if (!string.IsNullOrEmpty(receiverId))
            {
                await Clients.User(receiverId)
                    .SendAsync("ReceiveMessage", senderId, message, null, msg.Timestamp);
            }
        }

        // Hàm 2: Gửi THẺ SẢN PHẨM (Tự động gọi khi bấm nút Chat)
        public async Task SendProduct(string receiverId, int productId)
        {
            var senderId = Context.UserIdentifier;

            var product = await _context.SanPhams.FindAsync(productId);
            if (product == null) return;

            var productData = new
            {
                id = product.MaSP,
                name = product.TenSP,
                price = product.Gia,
                image = product.ImageUrl ?? "/images/default.png"
            };
            string json = JsonConvert.SerializeObject(productData);

            var msg = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = "Đang hỏi về sản phẩm",
                IsProduct = true,
                ProductId = productId,
                ProductJson = json
            };

            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            // ✅ Gửi thẻ sản phẩm cho bên nhận
            if (!string.IsNullOrEmpty(receiverId))
            {
                await Clients.User(receiverId)
                    .SendAsync("ReceiveMessage", senderId, "", productData, msg.Timestamp);
            }

            // ✅ Đồng thời gửi lại cho chính người gửi để hiển thị ngay
            await Clients.Caller
                .SendAsync("ReceiveMessage", senderId, "", productData, msg.Timestamp);
        }
    }
}