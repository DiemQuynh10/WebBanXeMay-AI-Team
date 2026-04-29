import json
import logging
import os
import re
from typing import Any

from openai import OpenAI
from pydantic import BaseModel, Field, ValidationError

logger = logging.getLogger("semantic_analyzer")

ANALYZER_MODEL = os.getenv("OPENAI_ANALYZER_MODEL") or os.getenv("OPENAI_MODEL") or "gpt-4o-mini"


class BudgetSpec(BaseModel):
    min: int | None = None
    max: int | None = None
    target: int | None = None
    currency: str = "VND"


class QueryAnalysis(BaseModel):
    intent: str = "other"
    brand_preference: list[str] = Field(default_factory=list)
    negative_preference: list[str] = Field(default_factory=list)
    budget: BudgetSpec = Field(default_factory=BudgetSpec)
    need: str = ""


_ANALYZE_SYSTEM_PROMPT = """
Bạn là bộ phân tích ngữ nghĩa cho chatbot tư vấn xe máy.

Nhiệm vụ:
1) Hiểu ngữ nghĩa câu hỏi tự nhiên (không dùng keyword matching thủ công).
2) Nhận diện phủ định và loại trừ rõ ràng (ví dụ: "không thích Honda", "không muốn xe ga", "đừng gợi ý Air Blade").
3) Tách thông tin thành JSON đúng schema, không thêm text ngoài JSON.

Schema bắt buộc:
{
  "intent": string,
  "brand_preference": string[],
  "negative_preference": string[],
  "budget": {
    "min": number|null,
    "max": number|null,
    "target": number|null,
    "currency": "VND"
  },
  "need": string
}

Quy ước:
- intent dùng một trong: recommendation, comparison, product_detail, price_lookup, order_lookup, other.
- brand_preference chỉ chứa sở thích tích cực.
- negative_preference chứa mọi sở thích phủ định theo brand/model/type.
- budget quy về VND số nguyên (ví dụ 40 triệu -> 40000000).
- need là mô tả ngắn gọn nhu cầu chính của user.
""".strip()

_openai_client: OpenAI | None = None


def _get_openai() -> OpenAI:
    global _openai_client
    if _openai_client is None:
        _openai_client = OpenAI(api_key=os.environ["OPENAI_API_KEY"])
    return _openai_client


def _extract_json_payload(raw: str) -> dict[str, Any]:
    text = (raw or "").strip()
    if not text:
        return {}

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    match = re.search(r"\{[\s\S]*\}", text)
    if not match:
        return {}

    try:
        return json.loads(match.group(0))
    except json.JSONDecodeError:
        return {}


def _normalize_analysis(analysis: QueryAnalysis) -> QueryAnalysis:
    analysis.brand_preference = _dedupe_non_empty(analysis.brand_preference)
    analysis.negative_preference = _dedupe_non_empty(analysis.negative_preference)

    # Nếu cùng xuất hiện ở cả positive và negative thì ưu tiên negative.
    negative_set = {x.lower() for x in analysis.negative_preference}
    analysis.brand_preference = [
        x for x in analysis.brand_preference if x.lower() not in negative_set
    ]

    if analysis.budget.min is not None and analysis.budget.max is not None:
        if analysis.budget.min > analysis.budget.max:
            analysis.budget.min, analysis.budget.max = analysis.budget.max, analysis.budget.min

    analysis.need = (analysis.need or "").strip()
    analysis.intent = (analysis.intent or "other").strip().lower() or "other"
    return analysis


def _dedupe_non_empty(values: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values or []:
        item = (value or "").strip()
        if not item:
            continue

        key = item.lower()
        if key in seen:
            continue

        seen.add(key)
        result.append(item)

    return result


def analyze_query(question: str) -> QueryAnalysis:
    question = (question or "").strip()
    if not question:
        return QueryAnalysis()

    try:
        completion = _get_openai().chat.completions.create(
            model=ANALYZER_MODEL,
            temperature=0,
            response_format={"type": "json_object"},
            messages=[
                {"role": "system", "content": _ANALYZE_SYSTEM_PROMPT},
                {
                    "role": "user",
                    "content": f"Phân tích câu sau và trả về JSON đúng schema:\n\n{question}",
                },
            ],
        )

        raw = completion.choices[0].message.content or "{}"
        payload = _extract_json_payload(raw)
        analysis = QueryAnalysis.model_validate(payload)
        return _normalize_analysis(analysis)
    except (ValidationError, KeyError, IndexError, TypeError) as ex:
        logger.warning("Analyze query returned malformed payload: %s", ex)
    except Exception as ex:
        logger.exception("Analyze query failed: %s", ex)

    return QueryAnalysis(intent="other", need=question)
