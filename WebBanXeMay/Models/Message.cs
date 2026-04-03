using System;
using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class Message
    {
        public int Id { get; set; }

        public string SenderId { get; set; }    // ID người gửi
        public string ReceiverId { get; set; }  // ID người nhận (Admin hoặc UserID)
        public string Content { get; set; }     // Nội dung tin nhắn
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // === PHẦN QUAN TRỌNG: đánh dấu tin là về sản phẩm ===
        public bool IsProduct { get; set; } = false; // Đánh dấu là thẻ sản phẩm
        public int? ProductId { get; set; }

        // Lưu JSON thông tin sản phẩm (Tên, Ảnh, Giá) vào đây. 
        // Để sau này lỡ Admin xóa sản phẩm thì trong đoạn chat vẫn hiện đúng thông tin cũ.
        public string? ProductJson { get; set; }

        // 🔴 NEW: tin này đã được ADMIN đọc chưa?
        // - Khách gửi tin cho admin  -> false
        // - Admin mở đoạn chat / load lịch sử -> mình set true
        // - Tin do chính admin gửi đi        -> true (coi như đã đọc)
        public bool IsReadByAdmin { get; set; } = false;
    }
}
