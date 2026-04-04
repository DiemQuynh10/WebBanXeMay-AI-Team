🚀 Giới thiệu

Hệ thống website bán xe máy tích hợp chatbot AI, gồm 3 phần:

WebBanXeMay → Website ASP.NET Core MVC
Chatbot.API → Backend xử lý chatbot
Chatbot-dev → Python RAG (tri thức)
⚙️ 1. Yêu cầu môi trường

Cần cài trước:

🔹 Bắt buộc
Visual Studio 2022
.NET 6 hoặc phù hợp project
SQL Server
Python 3.10+
Git
🔹 Khuyến nghị
SSMS (SQL Server Management Studio)
Postman
📥 2. Clone project
git clone https://github.com/YOUR_USERNAME/WebBanXeMay-AI.git
cd WebBanXeMay-AI
🔐 3. Cấu hình API Key (QUAN TRỌNG)
👉 Windows PowerShell
$env:OPENAI_API_KEY="your_api_key_here"
👉 Hoặc lưu lâu dài
setx OPENAI_API_KEY "your_api_key_here"

👉 Sau đó mở terminal mới

🧾 4. Tạo file cấu hình
4.1 Chatbot.API

Tạo file:

Chatbot.API/appsettings.json
{
  "ToolApi": {
    "BaseUrl": "https://localhost:7097"
  },
  "OpenAI": {
    "Model": "gpt-4o-mini"
  },
  "RagApi": {
    "BaseUrl": "http://localhost:8000"
  },
  "ConnectionStrings": {
    "ChatbotConnection": "Server=YOUR_SERVER;Database=ChatbotMemoryDb;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
4.2 WebBanXeMay

Tạo file:

WebBanXeMay/appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=WebBanXeMay;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "ChatbotApi": {
    "BaseUrl": "http://localhost:5127"
  }
}

👉 Thay YOUR_SERVER bằng:

LAPTOP-XXX\SQLEXPRESS
🗄️ 5. Tạo Database

Mở SQL Server → chạy:

CREATE DATABASE WebBanXeMay;
CREATE DATABASE ChatbotMemoryDb;
🧠 6. Chạy Python RAG
cd Chatbot-dev
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
▶️ Chạy server
uvicorn main:app --reload --port 8000

👉 Nếu file khác main.py → sửa lại cho đúng

🤖 7. Chạy Chatbot.API
cd Chatbot.API
dotnet restore
dotnet run

👉 Chạy ở:

http://localhost:5127
🌐 8. Chạy WebBanXeMay
cd WebBanXeMay
dotnet restore
dotnet run

Hoặc mở bằng Visual Studio → Run

🔄 9. Thứ tự chạy đúng

👉 BẮT BUỘC theo thứ tự:

SQL Server
Python RAG
Chatbot.API
WebBanXeMay
🔁 10. Luồng hoạt động

User → Web → Chatbot.API →
→ (Tool API hoặc RAG) →
→ trả kết quả về Web
