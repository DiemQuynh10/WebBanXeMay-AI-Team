import logging
from typing import Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from dotenv import load_dotenv

import rag
import semantic_analyzer
import sql_retriever

load_dotenv()

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("rag_service")

app = FastAPI(title="RAG Service", version="1.0.0")


class RagQueryRequest(BaseModel):
    query: str
    top_k: int = Field(default=4, ge=1, le=8)


class RagQueryResponse(BaseModel):
    success: bool
    context: str
    chunks: list[str] = []
    analysis: dict[str, Any] | None = None


class AnalyzeQueryRequest(BaseModel):
    question: str


class AnalyzeQueryResponse(BaseModel):
    success: bool
    analysis: dict[str, Any]


class HybridRouteRequest(BaseModel):
    query: str
    top_k: int = Field(default=4, ge=1, le=8)
    sql_take: int = Field(default=5, ge=1, le=12)


class HybridRouteResponse(BaseModel):
    success: bool
    route: str
    analysis: dict[str, Any]
    rag_context: str
    rag_chunks: list[str]
    sql_candidates: list[dict[str, Any]]
    merged_context: str


def _split_context(context: str) -> list[str]:
    if not context:
        return []
    return [c.strip() for c in context.split("\n\n---\n\n") if c.strip()]


def _resolve_route(intent: str) -> str:
    normalized_intent = (intent or "").strip().lower()

    if normalized_intent in {"price_lookup", "product_detail", "order_lookup"}:
        return "sql_priority"

    if normalized_intent in {"recommendation", "comparison"}:
        return "hybrid"

    return "rag_priority"


def _merge_context(sql_context: str, rag_context: str) -> str:
    sql_context = (sql_context or "").strip()
    rag_context = (rag_context or "").strip()

    if sql_context and rag_context:
        return f"{sql_context}\n\n---\n\n{rag_context}"
    return sql_context or rag_context


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


@app.post("/analyze/query", response_model=AnalyzeQueryResponse)
def analyze_query(request: AnalyzeQueryRequest):
    question = (request.question or "").strip()
    if not question:
        raise HTTPException(status_code=400, detail="Question không được để trống")

    try:
        analysis = semantic_analyzer.analyze_query(question)
        return AnalyzeQueryResponse(success=True, analysis=analysis.model_dump())
    except Exception as ex:
        logger.exception("Error while analyzing query.")
        raise HTTPException(status_code=500, detail=f"Lỗi phân tích query: {str(ex)}")


@app.post("/rag/query", response_model=RagQueryResponse)
def rag_query(request: RagQueryRequest):
    query = (request.query or "").strip()

    if not query:
        raise HTTPException(status_code=400, detail="Query không được để trống")

    try:
        analysis = semantic_analyzer.analyze_query(query)
        analysis_payload = analysis.model_dump()

        context = rag.search(
            query,
            top_k=int(request.top_k),
            analysis=analysis_payload,
        )

        if not context or context.strip() == "":
            return RagQueryResponse(
                success=True,
                context="",
                chunks=[],
                analysis=analysis_payload,
            )

        if context == "(Không tìm thấy dữ liệu)":
            return RagQueryResponse(
                success=True,
                context="",
                chunks=[],
                analysis=analysis_payload,
            )

        chunks = _split_context(context)

        return RagQueryResponse(
            success=True,
            context=context,
            chunks=chunks,
            analysis=analysis_payload,
        )
    except Exception as ex:
        logger.exception("Error while querying RAG.")
        raise HTTPException(status_code=500, detail=f"Lỗi truy vấn RAG: {str(ex)}")


@app.post("/chat/route", response_model=HybridRouteResponse)
def route_chat_query(request: HybridRouteRequest):
    query = (request.query or "").strip()

    if not query:
        raise HTTPException(status_code=400, detail="Query không được để trống")

    try:
        analysis = semantic_analyzer.analyze_query(query)
        analysis_payload = analysis.model_dump()
        route = _resolve_route(analysis.intent)

        sql_candidates: list[sql_retriever.ProductCandidate] = []
        rag_context = ""

        if route in {"hybrid", "sql_priority"}:
            sql_candidates = sql_retriever.search_products_semantic(
                analysis,
                take=int(request.sql_take),
                pool_size=max(int(request.sql_take) * 8, 20),
            )

        if route in {"hybrid", "rag_priority"} or not sql_candidates:
            rag_context = rag.search(
                query,
                top_k=int(request.top_k),
                analysis=analysis_payload,
            )
            if rag_context == "(Không tìm thấy dữ liệu)":
                rag_context = ""

        rag_chunks = _split_context(rag_context)
        sql_payload = [item.model_dump() for item in sql_candidates]
        sql_context = sql_retriever.format_products_as_context(sql_candidates)
        merged_context = _merge_context(sql_context, rag_context)

        return HybridRouteResponse(
            success=True,
            route=route,
            analysis=analysis_payload,
            rag_context=rag_context,
            rag_chunks=rag_chunks,
            sql_candidates=sql_payload,
            merged_context=merged_context,
        )
    except Exception as ex:
        logger.exception("Error while routing query.")
        raise HTTPException(status_code=500, detail=f"Lỗi router semantic: {str(ex)}")


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