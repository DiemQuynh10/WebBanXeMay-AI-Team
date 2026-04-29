# webbanxemay-ai-platform

Ten GitHub de xuat cho project: `webbanxemay-ai-platform`

Mo ta ngan: Nen tang ban xe may tich hop chatbot AI da kenh (Web + Telegram), ket hop Tool API va RAG Python.

## 1. Tong quan kien truc

Project gom 3 thanh phan chinh:

1. `WebBanXeMay`
- Website ASP.NET Core MVC cho nguoi dung.
- Co cac API tools cho san pham/don hang de chatbot goi.

2. `Chatbot.API`
- API hoi thoai AI (goi OpenAI + Tool API + RAG service).
- Quan ly hoi thoai web va webhook Telegram.

3. `Chatbot-dev`
- RAG service bang FastAPI (Python).
- Luu vector va truy van tri thuc qua ChromaDB.

## 2. Yeu cau moi truong

- .NET SDK 9.0
- Python 3.10+ (khuyen nghi 3.11)
- SQL Server
- ODBC Driver 17/18 for SQL Server (de script Python doc DB)
- Ngrok (neu test Telegram webhook)

## 3. Clone project

```bash
git clone https://github.com/<your-org>/webbanxemay-ai-platform.git
cd webbanxemay-ai-platform
git checkout dev
```

## 4. Cau hinh quan trong

Tao 2 file local (khong commit):

- `Chatbot.API/appsettings.Development.json`
- `WebBanXeMay/appsettings.Development.json`

Co the copy tu file mau:

```powershell
Copy-Item Chatbot.API/appsettings.example.json Chatbot.API/appsettings.Development.json
Copy-Item WebBanXeMay/appsettings.example.json WebBanXeMay/appsettings.Development.json
```

### 4.1 WebBanXeMay

Trong `WebBanXeMay/appsettings.Development.json`:

- `ConnectionStrings:DefaultConnection`: chuoi ket noi SQL Server
- `ToolApi:ApiKey`: khoa noi bo (dat giong ben Chatbot.API)
- `ChatbotApi:BaseUrl`: `http://localhost:5127`

### 4.2 Chatbot.API

Trong `Chatbot.API/appsettings.Development.json`:

- `ConnectionStrings:ChatbotConnection`: chuoi ket noi SQL Server
- `ToolApi:BaseUrl`: `http://localhost:5154`
- `ToolApi:ApiKey`: phai trung voi `WebBanXeMay:ToolApi:ApiKey`
- `OpenAI:ApiKey`: API key OpenAI
- `RagApi:BaseUrl`: `http://localhost:8000`
- `Telegram:*`: de trong neu chua dung Telegram

Luu y: `Chatbot.API` co doc bien tu `Chatbot-dev/.env` cho cac key nhu `OPENAI_API_KEY`, `TOOL_API_KEY`, `TELEGRAM_*`.

## 5. Setup va chay RAG (Python)

```powershell
cd Chatbot-dev
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Tao file `Chatbot-dev/.env` (vi du):

```env
OPENAI_API_KEY=your_openai_api_key
TOOL_API_KEY=your_shared_tool_api_key
```

Neu muon dong bo tri thuc tu DB vao `knowledge_base.txt`, chay:

```powershell
python db_to_kb.py
```

Chay RAG service:

```powershell
uvicorn chatbot:app --host 0.0.0.0 --port 8000 --reload
```

## 6. Chay Web va API

Tu thu muc goc project:

```powershell
dotnet restore WebBanXeMay.sln
dotnet build WebBanXeMay.sln
```

Mo 2 terminal rieng:

Terminal 1 - WebBanXeMay:

```powershell
dotnet run --project WebBanXeMay --launch-profile https
```

Terminal 2 - Chatbot.API:

```powershell
dotnet run --project Chatbot.API --launch-profile https
```

Port mac dinh:

- WebBanXeMay: `https://localhost:7097` va `http://localhost:5154`
- Chatbot.API: `https://localhost:7067` va `http://localhost:5127`
- RAG service: `http://localhost:8000`

## 7. Thu tu khoi dong de on dinh

1. SQL Server
2. RAG service (`Chatbot-dev`)
3. WebBanXeMay
4. Chatbot.API
5. (Tuy chon) ngrok + Telegram

## 8. Kiem tra nhanh

Kiem tra health RAG:

- `http://localhost:8000/health`

Kiem tra Swagger Chatbot.API:

- `https://localhost:7067/swagger`

Mo website:

- `https://localhost:7097`

Test cau hoi goi y:

- xe ga cho sinh vien
- xe cho nu 40 trieu
- air blade gia bao nhieu

## 9. Cau hinh Telegram webhook (tuy chon)

Mo tunnel cho Chatbot.API:

```powershell
ngrok http 7067
```

Dat trong `Chatbot.API/appsettings.Development.json`:

- `Telegram:WebhookUrl = https://<ngrok-domain>/api/telegram/webhook`
- `Telegram:SecretToken = <your-secret-token>`
- `Telegram:BotToken = <your-bot-token>`
- `Telegram:PublicWebBaseUrl = https://<public-domain-cua-WebBanXeMay>`

Neu WebBanXeMay chay local thi can mo them tunnel rieng cho web (vi du `ngrok http 7097`) va gan domain do vao `Telegram:PublicWebBaseUrl` de nut `Xem tren website` truy cap duoc.

## 10. Quy tac Git de lam viec nhom

```bash
git checkout dev
git pull
git checkout -b feature/ten-chuc-nang
```

Khong commit:

- `appsettings.Development.json`
- API key, token, webhook URL nhay cam
