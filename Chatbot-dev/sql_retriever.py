import logging
import math
import os
import re
import unicodedata
from typing import Any

import pyodbc
from openai import OpenAI
from pydantic import BaseModel

from semantic_analyzer import QueryAnalysis

logger = logging.getLogger("sql_retriever")

EMBED_MODEL = "text-embedding-3-small"

_openai_client: OpenAI | None = None


class ProductCandidate(BaseModel):
    id: int
    name: str
    brand: str
    category: str
    price: float
    stock: int
    tags: str = ""
    description: str = ""
    score: float = 0.0


def _get_openai() -> OpenAI:
    global _openai_client
    if _openai_client is None:
        _openai_client = OpenAI(api_key=os.environ["OPENAI_API_KEY"])
    return _openai_client


def _normalize_text(text: str) -> str:
    normalized = unicodedata.normalize("NFD", text or "")
    no_diacritics = "".join(ch for ch in normalized if unicodedata.category(ch) != "Mn")
    lowered = no_diacritics.replace("đ", "d").replace("Đ", "D").lower()
    return re.sub(r"\s+", " ", lowered.strip())


def _build_connection_string() -> str:
    server = os.getenv("SQL_SERVER", "HUYENPEA")
    database = os.getenv("SQL_DATABASE", "WebBanXeMay")
    driver = os.getenv("SQL_DRIVER", "ODBC Driver 17 for SQL Server")
    username = os.getenv("SQL_USERNAME", "").strip()
    password = os.getenv("SQL_PASSWORD", "").strip()

    if username and password:
        return (
            f"DRIVER={{{driver}}};"
            f"SERVER={server};"
            f"DATABASE={database};"
            f"UID={username};"
            f"PWD={password};"
            "TrustServerCertificate=yes;"
        )

    return (
        f"DRIVER={{{driver}}};"
        f"SERVER={server};"
        f"DATABASE={database};"
        "Trusted_Connection=yes;"
        "TrustServerCertificate=yes;"
    )


def _connect() -> pyodbc.Connection:
    return pyodbc.connect(_build_connection_string(), timeout=10)


def _build_product_profile(candidate: ProductCandidate) -> str:
    return (
        f"Tên xe: {candidate.name}. "
        f"Hãng: {candidate.brand}. "
        f"Loại: {candidate.category}. "
        f"Giá: {candidate.price:.0f} VND. "
        f"Tồn kho: {candidate.stock}. "
        f"Tags: {candidate.tags}. "
        f"Mô tả: {candidate.description}."
    )


def _cosine(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(y * y for y in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)


def _contains_negative_candidate(candidate: ProductCandidate, negatives: list[str]) -> bool:
    if not negatives:
        return False

    text = _normalize_text(
        f"{candidate.name} {candidate.brand} {candidate.category} {candidate.tags} {candidate.description}"
    )

    for neg in negatives:
        token = _normalize_text(neg)
        if not token:
            continue

        if re.search(rf"(?<![a-z0-9]){re.escape(token)}(?![a-z0-9])", text):
            return True

    return False


def _fetch_candidates(analysis: QueryAnalysis, pool_size: int) -> list[ProductCandidate]:
    pool_size = max(5, min(int(pool_size), 100))

    sql = f"""
SELECT TOP {pool_size}
    sp.MaSP,
    sp.TenSP,
    th.TenTH,
    l.TenLoai,
    CAST(sp.Gia AS FLOAT) AS Gia,
    sp.SoLuong,
    ISNULL(sp.Tags, '') AS Tags,
    ISNULL(sp.MoTa, '') AS MoTa
FROM SanPhams sp
INNER JOIN ThuongHieus th ON sp.MaTH = th.MaTH
INNER JOIN Loais l ON sp.MaLoai = l.MaLoai
WHERE sp.IsActive = 1
"""

    params: list[Any] = []

    if analysis.budget.min is not None:
        sql += " AND sp.Gia >= ?"
        params.append(analysis.budget.min)

    if analysis.budget.max is not None:
        sql += " AND sp.Gia <= ?"
        params.append(analysis.budget.max)

    include_brands = [x.strip() for x in analysis.brand_preference if x.strip()]
    if include_brands:
        placeholders = ", ".join(["?"] * len(include_brands))
        sql += f" AND th.TenTH IN ({placeholders})"
        params.extend(include_brands)

    exclude_brands: list[str] = []
    for item in analysis.negative_preference:
        value = item.strip()
        if not value:
            continue
        if " " not in value:
            exclude_brands.append(value)

    if exclude_brands:
        placeholders = ", ".join(["?"] * len(exclude_brands))
        sql += f" AND th.TenTH NOT IN ({placeholders})"
        params.extend(exclude_brands)

    if analysis.budget.target is not None:
        sql += " ORDER BY ABS(CAST(sp.Gia AS FLOAT) - ?), sp.SoLuong DESC"
        params.append(analysis.budget.target)
    else:
        sql += " ORDER BY sp.SoLuong DESC, sp.Gia ASC"

    with _connect() as conn:
        cursor = conn.cursor()
        rows = cursor.execute(sql, params).fetchall()

    candidates: list[ProductCandidate] = []
    for row in rows:
        candidates.append(
            ProductCandidate(
                id=int(row[0]),
                name=(row[1] or "").strip(),
                brand=(row[2] or "").strip(),
                category=(row[3] or "").strip(),
                price=float(row[4] or 0),
                stock=int(row[5] or 0),
                tags=(row[6] or "").strip(),
                description=(row[7] or "").strip(),
            )
        )

    return candidates


def _semantic_rank(candidates: list[ProductCandidate], analysis: QueryAnalysis) -> list[ProductCandidate]:
    if not candidates:
        return []

    query_text_parts = [
        f"Intent: {analysis.intent}",
        f"Nhu cầu: {analysis.need or 'tìm xe phù hợp'}",
    ]

    if analysis.brand_preference:
        query_text_parts.append(f"Ưu tiên hãng: {', '.join(analysis.brand_preference)}")
    if analysis.negative_preference:
        query_text_parts.append(f"Loại trừ: {', '.join(analysis.negative_preference)}")
    if analysis.budget.min is not None or analysis.budget.max is not None or analysis.budget.target is not None:
        query_text_parts.append(
            f"Ngân sách: min={analysis.budget.min}, max={analysis.budget.max}, target={analysis.budget.target}"
        )

    query_text = ". ".join(query_text_parts)

    profiles = [_build_product_profile(item) for item in candidates]
    embedding_inputs = [query_text] + profiles

    try:
        emb_resp = _get_openai().embeddings.create(model=EMBED_MODEL, input=embedding_inputs)
        vectors = [d.embedding for d in emb_resp.data]
        query_vector = vectors[0]
        profile_vectors = vectors[1:]
    except Exception as ex:
        logger.warning("Embedding for SQL ranking failed, fallback to deterministic scoring: %s", ex)
        return candidates

    preferred_brands = {_normalize_text(x) for x in analysis.brand_preference}

    for item, profile_vector in zip(candidates, profile_vectors):
        semantic_score = _cosine(query_vector, profile_vector)
        score = semantic_score

        if preferred_brands and _normalize_text(item.brand) in preferred_brands:
            score += 0.12

        if analysis.budget.target is not None:
            delta = abs(item.price - analysis.budget.target)
            budget_bonus = max(0.0, 0.12 - (delta / max(1.0, analysis.budget.target)) * 0.12)
            score += budget_bonus

        if item.stock > 0:
            score += 0.03

        item.score = round(score, 6)

    return sorted(candidates, key=lambda x: x.score, reverse=True)


def search_products_semantic(
    analysis: QueryAnalysis,
    take: int = 5,
    pool_size: int = 40,
) -> list[ProductCandidate]:
    try:
        candidates = _fetch_candidates(analysis, pool_size=pool_size)
    except Exception as ex:
        logger.exception("SQL fetch failed: %s", ex)
        return []

    filtered = [
        c for c in candidates if not _contains_negative_candidate(c, analysis.negative_preference)
    ]

    ranked = _semantic_rank(filtered, analysis)
    return ranked[: max(1, min(int(take), 12))]


def format_products_as_context(products: list[ProductCandidate]) -> str:
    if not products:
        return ""

    lines = ["Dữ liệu sản phẩm realtime từ SQL:"]
    for idx, item in enumerate(products, start=1):
        lines.append(
            (
                f"{idx}. {item.name} | Hãng: {item.brand} | Loại: {item.category} | "
                f"Giá: {item.price:,.0f} VND | Tồn: {item.stock}"
            )
        )

    return "\n".join(lines)
