# WebBanXeMay-AI-Team

Hệ thống website bán xe máy tích hợp chatbot AI đa kênh, hỗ trợ:
- Tư vấn chọn xe theo nhu cầu
- Tra cứu giá và tồn kho
- Hỏi đáp sử dụng tri thức từ RAG
- Tương tác qua Website và Telegram

---

# 1. Kiến trúc tổng thể

Dự án gồm 3 thành phần chính:

## 1.1 WebBanXeMay
Website chính dành cho người dùng, hiển thị giao diện bán xe và chatbot.

## 1.2 Chatbot.API
API trung gian xử lý hội thoại, gọi mô hình AI, kết nối dữ liệu nghiệp vụ và Telegram webhook.

## 1.3 Chatbot-dev
Dịch vụ RAG viết bằng Python.

---

# 2. Clone project

```bash
git clone <LINK_REPO>
cd WebBanXeMay-AI-Team
git checkout dev
```

---

# 3. Cấu hình quan trọng

## Tạo file (KHÔNG commit lên Git)

- Chatbot.API/appsettings.Development.json
- WebBanXeMay/appsettings.Development.json

---

# 4. Chạy hệ thống

## Web
```bash
dotnet run --project WebBanXeMay
```

## API
```bash
dotnet run --project Chatbot.API
```

## RAG
```bash
cd Chatbot-dev
python rag.py
```

## Telegram (ngrok)
```bash
ngrok http 7066
```

---

# 5. Thứ tự chạy

1. SQL Server  
2. WebBanXeMay  
3. Chatbot.API  
4. RAG Python  
5. ngrok  
6. test bot  

---

# 6. Test nhanh

- xe ga cho sinh viên  
- xe cho nữ 40 triệu  
- air blade giá bao nhiêu  

---

# 7. Workflow team

```bash
git checkout dev
git pull
git checkout -b feature/ten-chuc-nang
```

---

# 8. Lưu ý

Không commit:
- appsettings.Development.json
- API key
- Token
