import logging
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from dotenv import load_dotenv
import rag

load_dotenv()

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("rag_service")

app = FastAPI(title="RAG Service", version="1.0.0")


class RagQueryRequest(BaseModel):
    query: str
    top_k: int = 4


class RagQueryResponse(BaseModel):
    success: bool
    context: str
    chunks: list[str] = []


@app.on_event("startup")
def startup_event():
    try:
        count = rag.build_index(force=False)
        logger.info(f"RAG index loaded successfully. Chunks indexed: {count}")
    except Exception as ex:
        logger.exception("Failed to initialize RAG index.")
        raise ex


@app.get("/")
def home():
    try:
        collection = rag._get_collection()
        count = collection.count()
    except Exception:
        count = 0

    return {
        "success": True,
        "message": "RAG service is running",
        "chunks_indexed": count
    }


@app.get("/health")
def health():
    try:
        collection = rag._get_collection()
        count = collection.count()
        return {
            "success": True,
            "status": "healthy",
            "chunks_indexed": count
        }
    except Exception as ex:
        raise HTTPException(status_code=500, detail=f"RAG health check failed: {str(ex)}")


@app.post("/rag/query", response_model=RagQueryResponse)
def rag_query(request: RagQueryRequest):
    query = (request.query or "").strip()

    if not query:
        raise HTTPException(status_code=400, detail="Query không được để trống")

    try:
        context = rag.search(query, top_k=request.top_k)

        if not context or context.strip() == "":
            return RagQueryResponse(
                success=True,
                context="",
                chunks=[]
            )

        if context == "(Không tìm thấy dữ liệu)":
            return RagQueryResponse(
                success=True,
                context="",
                chunks=[]
            )

        chunks = [c.strip() for c in context.split("\n\n---\n\n") if c.strip()]

        return RagQueryResponse(
            success=True,
            context=context,
            chunks=chunks
        )
    except Exception as ex:
        logger.exception("Error while querying RAG.")
        raise HTTPException(status_code=500, detail=f"Lỗi truy vấn RAG: {str(ex)}")


@app.post("/rag/rebuild")
def rebuild_rag():
    try:
        count = rag.build_index(force=True)
        return {
            "success": True,
            "message": f"Đã build lại index thành công với {count} chunks"
        }
    except Exception as ex:
        logger.exception("Error while rebuilding RAG index.")
        raise HTTPException(status_code=500, detail=f"Lỗi rebuild RAG: {str(ex)}")