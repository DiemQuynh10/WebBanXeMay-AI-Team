"""
RAG Module - Retrieval-Augmented Generation
Đọc knowledge_base.txt, chia nhỏ thành chunks thông minh, lưu vector vào ChromaDB.
Tối ưu cho bài toán tư vấn xe máy theo nhu cầu và so sánh giữa các mẫu.
"""

import os
import re
import unicodedata
from pathlib import Path
from typing import List, Dict, Any
from openai import OpenAI
import chromadb

# -------------------------------------------------------
# CONFIG
# -------------------------------------------------------
KB_FILE = Path(__file__).parent / "knowledge_base.txt"
CHROMA_DIR = Path(__file__).parent / "chroma_db"
COLLECTION = "knowledge"
EMBED_MODEL = "text-embedding-3-small"
TOP_K = 5

_openai_client = None
_chroma_collection = None


# -------------------------------------------------------
# OPENAI CLIENT
# -------------------------------------------------------
def _get_openai():
    global _openai_client
    if _openai_client is None:
        _openai_client = OpenAI(api_key=os.environ["OPENAI_API_KEY"])
    return _openai_client


# -------------------------------------------------------
# CHROMA COLLECTION
# -------------------------------------------------------
def _get_collection():
    global _chroma_collection

    if _chroma_collection is None:
        client = chromadb.PersistentClient(path=str(CHROMA_DIR))
        _chroma_collection = client.get_or_create_collection(
            name=COLLECTION,
            metadata={"hnsw:space": "cosine"},
        )

    return _chroma_collection


# -------------------------------------------------------
# HELPERS
# -------------------------------------------------------
def _normalize_text(text: str) -> str:
    normalized = unicodedata.normalize("NFD", text or "")
    without_diacritics = "".join(ch for ch in normalized if unicodedata.category(ch) != "Mn")
    lowered = without_diacritics.replace("đ", "d").replace("Đ", "D").lower()
    return re.sub(r"\s+", " ", lowered.strip())


def _extract_query_keywords(query: str) -> List[str]:
    stopwords = {
        "xe", "la", "hay", "cho", "toi", "minh", "ban", "giup", "voi", "nao",
        "co", "khong", "duoc", "nen", "mua", "tu", "den", "tam", "khoang", "quanh",
        "di", "hoc", "lam", "de", "va", "hoac", "nhung", "nhung"
    }

    words = re.findall(r"[a-z0-9]{2,}", _normalize_text(query))
    return [w for w in words if w not in stopwords]


def _extract_product_name(block: str) -> str | None:
    match = re.search(r"^\[(.+?)\]", block.strip(), flags=re.MULTILINE)
    return match.group(1).strip() if match else None


def _infer_section(block: str) -> str:
    lower = block.lower()

    if lower.startswith("--- tri thức so sánh"):
        return "comparison"
    if lower.startswith("--- tri thức tư vấn theo nhu cầu"):
        return "advisory"
    if lower.startswith("--- tri thức tư vấn chung"):
        return "general_advisory"
    if lower.startswith("== côn tay =="):
        return "manual_section"
    if lower.startswith("== tay ga =="):
        return "scooter_section"
    if lower.startswith("== xe số =="):
        return "underbone_section"
    if block.strip().startswith("["):
        return "product"

    return "general"


# -------------------------------------------------------
# CHUNKING THÔNG MINH
# -------------------------------------------------------
def _split_into_chunks(text: str, chunk_size: int = 650) -> List[Dict[str, Any]]:
    """
    Chia chunk theo block lớn:
    - Mỗi sản phẩm [Tên xe] là 1 chunk riêng
    - Mỗi section tri thức chung / so sánh / theo nhu cầu là 1 hoặc vài chunk
    """
    chunks: List[Dict[str, Any]] = []
    chunk_id = 0

    text = text.strip()
    if not text:
        return []

    lines = text.splitlines()
    current_block: List[str] = []

    def flush_block(block_lines: List[str]):
        nonlocal chunk_id
        block = "\n".join(block_lines).strip()
        if not block:
            return

        section = _infer_section(block)
        product_name = _extract_product_name(block)

        def add_chunk(chunk_text: str):
            nonlocal chunk_id
            if not chunk_text.strip():
                return
            chunks.append({
                "id": f"chunk_{chunk_id}",
                "text": chunk_text.strip(),
                "section": section,
                "product_name": product_name or ""
            })
            chunk_id += 1

        if len(block) <= chunk_size:
            add_chunk(block)
            return

        # Nếu block dài quá thì ưu tiên tách theo tiêu đề phụ, sau đó theo đoạn trống
        sub_blocks: List[str] = []
        current: List[str] = []
        for line in block.splitlines():
            is_sub_heading = bool(re.match(r"^\s*[A-ZÁÀẢÃẠÂĂĐÊÔƠƯÍÌỈĨỊÓÒỎÕỌÚÙỦŨỤÝỲỶỸỴa-z].+:\s*$", line))
            if is_sub_heading and current:
                sub_blocks.append("\n".join(current).strip())
                current = [line]
            else:
                current.append(line)
        if current:
            sub_blocks.append("\n".join(current).strip())

        paragraphs: List[str] = []
        for sub in sub_blocks:
            paragraphs.extend([p for p in re.split(r"\n{2,}", sub) if p.strip()])

        buffer = ""
        for para in paragraphs:
            if len(buffer) + len(para) + 2 <= chunk_size:
                buffer += ("\n\n" if buffer else "") + para
            else:
                if buffer.strip():
                    add_chunk(buffer)
                buffer = para

        if buffer.strip():
            add_chunk(buffer)

    for line in lines:
        stripped = line.strip()

        # Bắt đầu block mới khi gặp [Tên xe], bất kỳ tiêu đề '---', hoặc == section ==
        is_new_block = (
            stripped.startswith("[")
            or stripped.startswith("---")
            or stripped.startswith("== ")
        )

        if is_new_block and current_block:
            flush_block(current_block)
            current_block = [line]
        else:
            current_block.append(line)

    if current_block:
        flush_block(current_block)

    return chunks


# -------------------------------------------------------
# EMBEDDING
# -------------------------------------------------------
def _embed(text: str):
    resp = _get_openai().embeddings.create(
        model=EMBED_MODEL,
        input=text
    )
    return resp.data[0].embedding


# -------------------------------------------------------
# BUILD INDEX
# -------------------------------------------------------
def build_index(force: bool = False):
    collection = _get_collection()

    if force:
        try:
            existing = collection.get()
            if existing["ids"]:
                collection.delete(ids=existing["ids"])
        except Exception:
            pass

    if not force and collection.count() > 0:
        print(f"[RAG] Index đã có {collection.count()} chunks")
        return collection.count()

    if not KB_FILE.exists():
        raise FileNotFoundError(f"Không tìm thấy {KB_FILE}")

    text = KB_FILE.read_text(encoding="utf-8")
    chunks = _split_into_chunks(text)

    print(f"[RAG] Đang index {len(chunks)} chunks...")

    if not chunks:
        raise ValueError("knowledge_base.txt không có nội dung hợp lệ")

    ids = []
    texts = []
    embeddings = []
    metadatas = []

    for chunk in chunks:
        print("→", chunk["text"][:100].replace("\n", " "), "...")
        emb = _embed(chunk["text"])

        ids.append(chunk["id"])
        texts.append(chunk["text"])
        embeddings.append(emb)
        metadatas.append({
            "section": chunk["section"],
            "product_name": chunk["product_name"]
        })

    collection.add(
        ids=ids,
        documents=texts,
        embeddings=embeddings,
        metadatas=metadatas
    )

    print(f"[RAG] ✅ Đã index {len(ids)} chunks")
    return len(ids)


# -------------------------------------------------------
# QUERY ENRICHMENT
# -------------------------------------------------------
def _extract_product_names_from_query(query: str) -> List[str]:
    alias_to_product = {
        "honda vision": "Honda Vision",
        "honda air blade": "Honda Air Blade",
        "honda pcx": "Honda PCX",
        "honda sh": "Honda SH 150i",
        "sh 150i": "Honda SH 150i",
        "sh150i": "Honda SH 150i",
        "honda future": "Honda Future",
        "honda wave": "Honda Wave",
        "yamaha freego": "Yamaha Freego",
        "yamaha latte": "Yamaha Latte",
        "yamaha grande": "Yamaha Grande",
        "yamaha jupiter": "Yamaha Jupiter",
        "yamaha sirius": "Yamaha Sirius",
        "yamaha exciter": "Yamaha Exciter",
        "piaggio zip 100": "Piaggio Zip 100",
        "piaggio liberty 125": "Piaggio Liberty 125",
        "piaggio medley 150": "Piaggio Medley 150",
        "piaggio vespa lx": "Piaggio Vespa LX",
        "suzuki address 110": "Suzuki Address 110",
        "suzuki burgman 125": "Suzuki Burgman 125",
        "suzuki impulse 125": "Suzuki Impulse 125",
        "suzuki axelo 125": "Suzuki Axelo 125",
        "sym shark mini": "SYM Shark Mini",
        "sym attila venus": "SYM Attila Venus",
        "sym elegant 110": "SYM Elegant 110",
    }

    normalized_query = _normalize_text(query)
    matched: List[str] = []
    for alias, product in alias_to_product.items():
        if re.search(rf"(?<![a-z0-9]){re.escape(alias)}(?![a-z0-9])", normalized_query):
            matched.append(product)

    return list(dict.fromkeys(matched))


def _build_query_variants(query: str) -> List[str]:
    variants = [query.strip()]
    lower = _normalize_text(query)

    if any(k in lower for k in ["tu van", "phu hop", "goi y", "nen mua"]):
        variants.append(query + " điểm mạnh riêng từng mẫu xe, trường hợp nên chọn")

    if any(k in lower for k in ["duoi m", "1m", "cm", "de chong chan", "nguoi thap", "nho con"]):
        variants.append(query + " xe nào dễ chống chân, gọn, yên thấp")

    if "cop rong" in lower:
        variants.append(query + " mẫu nào cốp rộng, tiện mang đồ, thực dụng")

    if "di lam" in lower:
        variants.append(query + " mẫu nào hợp đi làm hằng ngày, thực dụng, linh hoạt đi phố")

    if "sinh vien" in lower:
        variants.append(query + " mẫu nào hợp sinh viên, giá hợp lý, dễ dùng, tiết kiệm")

    return list(dict.fromkeys(variants))


def _extract_intent_tags(query: str) -> List[str]:
    lower = _normalize_text(query)
    tags: List[str] = []

    if "sinh vien" in lower:
        tags.append("student")
    if "di hoc" in lower:
        tags.append("school")
    if "di lam" in lower:
        tags.append("work")
    if "cop rong" in lower:
        tags.append("storage")
    if any(k in lower for k in ["duoi m", "1m", "cm", "de chong chan", "nguoi thap", "nho con"]):
        tags.append("low_seat")
    if "nu" in lower:
        tags.append("female")
    if "nam" in lower:
        tags.append("male")
    if "xe ga" in lower or "tay ga" in lower:
        tags.append("scooter")
    if "xe so" in lower:
        tags.append("underbone")
    if "con tay" in lower:
        tags.append("manual")
    if "tiet kiem xang" in lower:
        tags.append("fuel_saving")

    return tags


def _score_doc_by_intent(doc: str, tags: List[str]) -> float:
    lower = _normalize_text(doc)
    bonus = 0.0

    if "student" in tags and any(k in lower for k in ["sinh vien", "di hoc", "hoc sinh"]):
        bonus += 0.08

    if "school" in tags and "di hoc" in lower:
        bonus += 0.06

    if "work" in tags and "di lam" in lower:
        bonus += 0.08

    if "storage" in tags and any(k in lower for k in ["cop rong", "mang do", "tien ich"]):
        bonus += 0.10

    if "low_seat" in tags and any(k in lower for k in ["yen thap", "de chong chan", "nho gon", "nguoi thap", "nho con"]):
        bonus += 0.12

    if "female" in tags and any(k in lower for k in ["nu", "dang mem", "nu tinh"]):
        bonus += 0.06

    if "male" in tags and any(k in lower for k in ["nam", "manh me", "the thao"]):
        bonus += 0.06

    if "fuel_saving" in tags and any(k in lower for k in ["tiet kiem", "it hao", "chi phi"]):
        bonus += 0.06

    if "scooter" in tags and any(k in lower for k in ["tay ga", "xe ga"]):
        bonus += 0.04

    if "underbone" in tags and "xe so" in lower:
        bonus += 0.04

    if "manual" in tags and "con tay" in lower:
        bonus += 0.04

    return bonus


def _score_doc_by_keyword_overlap(doc: str, keywords: List[str]) -> float:
    if not keywords:
        return 0.0

    normalized_doc = _normalize_text(doc)
    matched = sum(1 for k in keywords if k in normalized_doc)
    if matched == 0:
        return 0.0

    overlap = matched / max(1, len(keywords))
    return min(0.18, overlap * 0.18)


# -------------------------------------------------------
# SEARCH
# -------------------------------------------------------
def search(query: str, top_k: int = TOP_K):
    collection = _get_collection()
    top_k = max(1, int(top_k))

    if collection.count() == 0:
        build_index()

    count = collection.count()
    if count == 0:
        return "(Không tìm thấy dữ liệu)"

    variants = _build_query_variants(query)
    product_names = _extract_product_names_from_query(query)
    intent_tags = _extract_intent_tags(query)
    query_keywords = _extract_query_keywords(query)

    scored_docs: Dict[str, Dict[str, Any]] = {}

    for q in variants:
        query_embedding = _embed(q)

        results = collection.query(
            query_embeddings=[query_embedding],
            n_results=min(max(top_k * 3, 10), count),
            include=["documents", "distances", "metadatas"]
        )

        docs = results["documents"][0]
        distances = results["distances"][0]
        metadatas = results["metadatas"][0]

        for doc, dist, meta in zip(docs, distances, metadatas):
            if not doc:
                continue

            score = 1.0 - float(dist)  # cosine similarity càng cao càng tốt
            section = meta.get("section", "")
            product_name = meta.get("product_name", "")

            # Ưu tiên section tri thức so sánh / theo nhu cầu cho bài toán tư vấn
            if section == "comparison":
                score += 0.12
            elif section == "advisory":
                score += 0.10
            elif section == "general_advisory":
                score += 0.05

            # Ưu tiên block đúng tên xe trong query
            if product_names and product_name in product_names:
                score += 0.18

            # Nếu query thiên về tư vấn, ưu tiên chunk có "Gợi ý tư vấn"
            if "goi y tu van" in _normalize_text(doc):
                score += 0.06

            # Ưu tiên theo intent tags
            score += _score_doc_by_intent(doc, intent_tags)
            score += _score_doc_by_keyword_overlap(doc, query_keywords)

            key = _normalize_text(doc.strip())
            if key not in scored_docs or score > scored_docs[key]["score"]:
                scored_docs[key] = {
                    "score": score,
                    "section": section,
                    "product_name": product_name,
                    "doc": doc.strip()
                }

    if not scored_docs:
        return "(Không tìm thấy dữ liệu)"

    ranked = sorted(
        scored_docs.values(),
        key=lambda x: x["score"],
        reverse=True
    )

    # Giữ diversity cân bằng giữa comparison / advisory / product
    selected = []
    seen_products = set()
    section_limits = {
        "comparison": 1,
        "advisory": 2,
        "general_advisory": 1,
        "manual_section": 1,
        "scooter_section": 1,
        "underbone_section": 1,
        "product": max(1, top_k)
    }
    section_counts: Dict[str, int] = {}

    for item in ranked:
        section = item["section"]
        product_name = item["product_name"]

        current_count = section_counts.get(section, 0)
        limit = section_limits.get(section, top_k)

        if current_count >= limit:
            continue

        if product_name:
            if product_name in seen_products:
                continue
            seen_products.add(product_name)

        selected.append(item["doc"])
        section_counts[section] = current_count + 1

        if len(selected) >= top_k:
            break

    if not selected:
        selected = [ranked[0]["doc"]]

    return "\n\n---\n\n".join(selected)


# -------------------------------------------------------
# MAIN
# -------------------------------------------------------
if __name__ == "__main__":
    from dotenv import load_dotenv
    load_dotenv()

    print("=== BUILD INDEX ===")
    n = build_index(force=True)
    print(f"Tổng số chunks: {n}\n")

    print("=== THỬ TÌM KIẾM ===")
    queries = [
        "xe ga cho sinh viên",
        "tư vấn xe khoảng 40 triệu cho nữ hơi lùn dưới m50",
        "ưu tiên cốp rộng đi làm hàng ngày",
        "còn honda thì sao"
    ]

    for q in queries:
        print(f"\n❓ {q}")
        print(search(q, top_k=4))
        print("-" * 60)