# 🏍️ Website Bán Xe Máy Tích Hợp AI Chatbot

Dự án xây dựng website bán xe máy bằng ASP.NET Core MVC kết hợp AI Chatbot nhằm hỗ trợ khách hàng tìm kiếm thông tin và lựa chọn sản phẩm phù hợp.

Điểm nổi bật của dự án là chatbot không chỉ trả lời hội thoại thông thường mà còn có khả năng làm việc với dữ liệu thực tế của hệ thống như sản phẩm, tồn kho và đơn hàng.

Dự án được thực hiện bởi nhóm 2 thành viên. Mình phụ trách phần chatbot và website, thành viên còn lại phụ trách Telegram Bot.

---

# Công nghệ sử dụng

## Backend

* ASP.NET Core MVC
* Entity Framework Core
* SQL Server
* C#

## AI Chatbot

* OpenAI API
* ChromaDB
* Retrieval-Augmented Generation (RAG)
* Conversation Memory

## Frontend

* Razor View
* Bootstrap
* JavaScript
* AJAX

---

# Phần mình thực hiện

Trong dự án này mình phụ trách:

* Thiết kế luồng xử lý chatbot
* Xây dựng cơ chế Intent Routing
* Xây dựng Conversation Memory
* Tích hợp OpenAI API
* Xây dựng chức năng tư vấn sản phẩm
* Xây dựng chức năng so sánh sản phẩm
* Xây dựng chức năng kiểm tra tồn kho
* Xây dựng chức năng tra cứu đơn hàng
* Tích hợp chatbot vào website
* Phát triển các chức năng web chính

---

# Một số bài toán đã giải quyết

## Ghi nhớ ngữ cảnh hội thoại

Ví dụ:

> Tư vấn xe khoảng 30 triệu

Chatbot đề xuất:

* Honda Vision
* Yamaha Latte

Sau đó người dùng chỉ cần nhập:

> So sánh xe đầu tiên với xe thứ hai

Chatbot vẫn hiểu được sản phẩm nào đang được nhắc tới mà không cần người dùng nhập lại tên xe.

---

## Kết hợp AI và dữ liệu thực tế

Các thông tin như:

* Giá sản phẩm
* Tồn kho
* Trạng thái đơn hàng

được lấy trực tiếp từ cơ sở dữ liệu thay vì để AI tự tạo câu trả lời.

Cách tiếp cận này giúp giảm đáng kể tình trạng trả lời sai thông tin (hallucination) và tăng độ chính xác khi hỗ trợ khách hàng.

---

## Kiến trúc Chatbot

Chatbot được xây dựng theo mô hình Hybrid AI:

* Rule-based xử lý nghiệp vụ
* OpenAI hỗ trợ hiểu ngôn ngữ tự nhiên
* RAG hỗ trợ truy xuất thông tin liên quan
* Conversation Memory hỗ trợ hội thoại nhiều lượt

Mục tiêu là đảm bảo tính linh hoạt của AI nhưng vẫn giữ được độ chính xác khi xử lý dữ liệu thực tế.

---

# Hình ảnh hệ thống

## Trang chủ

![Home Page](docs/home-page.png)

## Danh sách sản phẩm

![Product Catalog](docs/product-page.png)

## Chatbot tư vấn sản phẩm

![Recommendation](docs/chatbot-recommendation.png)

## Chatbot so sánh sản phẩm

![Comparison](docs/chatbot-comparison.png)

## Quản lý đơn hàng

![Order Management](docs/order-management.png)

## Dashboard quản trị

![Dashboard](docs/admin-dashboard.png)

---

# Cấu trúc dự án

```text
WebBanXeMay
│
├── WebBanXeMay/
├── Chatbot.API/
├── Chatbot.API.Tests/
├── docs/
└── WebBanXeMay.sln
```

---

# Thành viên thực hiện

## Vai trò của tôi

Vai trò:

* Thiết kế kiến trúc chatbot
* Xây dựng Intent Routing
* Xây dựng Conversation Memory
* Tích hợp OpenAI API
* Phát triển các chức năng website

## Thành viên còn lại

Vai trò:

* Phát triển Telegram Bot
* Tích hợp chatbot trên Telegram

```
---

**Mục tiêu học tập**

- Tìm hiểu cách kết hợp AI vào ứng dụng web thực tế
- Xây dựng chatbot hỗ trợ khách hàng dựa trên dữ liệu doanh nghiệp
- Nâng cao kỹ năng ASP.NET Core MVC, Entity Framework Core và tích hợp API
```
