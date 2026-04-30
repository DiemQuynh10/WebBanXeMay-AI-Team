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
TOP_K = 8
RAG_SCHEMA_VERSION = "structured-v2"

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


def _extract_product_name(block: str) -> str | None:
    match = re.search(r"^\[(.+?)\]", block.strip(), flags=re.MULTILINE)
    return match.group(1).strip() if match else None


def _infer_domain(text: str) -> str:
    lower = _normalize_text(text)
    domains = {
        "installment": ["tra gop", "lai suat", "tra truoc", "vay", "tin dung", "ngan hang", "hd saison", "fe credit", "home credit", "tat toan"],
        "warranty": ["bao hanh", "chinh hang", "dong co", "khung xe", "phu tung", "khong bao hanh"],
        "maintenance": ["bao duong", "thay nhot", "bugi", "loc gio", "day curoa", "dinh ky"],
        "paperwork": ["giay to", "bien so", "ca vet", "dang ky", "truoc ba", "cu tru", "ct07", "vneid", "bao bien"],
        "return_policy": ["doi tra", "tra xe", "hoan tien", "khau hao", "hoa don vat", "lan banh", "nguyen tem"],
        "delivery": ["giao hang", "van chuyen", "tan noi", "noi thanh", "ngoai thanh"],
        "insurance": ["bao hiem", "tnds", "tai nan", "mat cap", "boi thuong"],
        "promotion": ["khuyen mai", "uu dai", "giam", "tang", "sinh vien"],
        "pricing": ["gia lan banh", "bao giay", "gia niem yet", "phi cap bien", "le phi truoc ba"],
        "deposit": ["dat coc", "giu xe", "hoan coc"],
        "tradein": ["thu cu", "doi moi", "trade-in", "len doi", "dinh gia"],
        "payment": ["thanh toan", "chuyen khoan", "quet the", "visa", "mastercard", "phi giao dich", "online"],
        "testride": ["lai thu", "test ride", "bang lai", "a1", "a2"],
        "technology": ["abs", "cbs", "smartkey", "chia khoa", "fi", "phun xang"],
        "connected_app": ["my honda", "y-connect", "app", "ung dung", "bluetooth", "bao duong dien tu"],
        "rescue": ["cuu ho", "thung lop", "chet may", "mat chia khoa", "het xang", "khan cap"],
        "faq": ["hao xang", "ton xang", "bao duong o que", "bao hanh toan quoc", "co san", "giao ngay"],
        "fengshui": ["phong thuy", "menh", "mau xe", "hop mau", "kim", "moc", "thuy", "hoa", "tho"],
    }

    for domain, keywords in domains.items():
        if any(k in lower for k in keywords):
            return domain

    return "general"


def _infer_slot(text: str, domain: str) -> str:
    lower = _normalize_text(text)
    slots = {
        "interest": ["lai suat", "0%", "0.5", "1.2", "1.5"],
        "zero_interest": ["0%", "0 %", "khong lai", "lai suat 0"],
        "down_payment": ["tra truoc", "down payment", "0 dong", "20%", "50%"],
        "loan_term": ["ky han", "ki han", "thoi gian vay", "may thang", "bao lau", "12", "60 thang"],
        "monthly_payment": ["hang thang", "moi thang", "1.5 trieu"],
        "approval_time": ["duyet", "15", "30 phut", "nhan xe"],
        "early_settlement": ["tat toan", "phi phat", "du no goc"],
        "conditions": ["dieu kien", "do tuoi", "thu nhap", "cic", "no xau", "bao lanh"],
        "documents": ["ho so", "giay to", "cmnd", "cccd", "ho khau", "kt3", "hop dong lao dong", "sao ke"],
        "process": ["quy trinh", "buoc", "ky hop dong", "tham dinh"],
        "deposit": ["dat coc", "giu xe", "hoan coc", "khong duoc duyet"],
        "deposit_amount": ["muc coc", "tien coc", "1.000.000", "3.000.000"],
        "listed_price": ["gia niem yet", "gia xe", "da co vat"],
        "onroad_price": ["gia lan banh", "bao giay", "ra bien", "chay ra duong"],
        "plate_fee": ["phi cap bien", "bien so", "2-4 trieu", "ho khau"],
        "registration_tax": ["le phi truoc ba", "truoc ba", "10-15%", "5%", "2%"],
        "plate_identity": ["bien so dinh danh", "giu lai bien", "di theo nguoi"],
        "warranty_period": ["bao hanh", "nam", "km", "khong gioi han"],
        "warranty_engine": ["dong co"],
        "warranty_frame": ["khung xe", "nut gay", "moi han"],
        "warranty_parts": ["phu tung", "linh kien"],
        "warranty_conditions": ["dieu kien bao hanh", "trung tam uy quyen", "phieu giay", "tu y sua chua"],
        "warranty_exclusion": ["khong bao hanh", "hao mon", "lop xe", "ma phanh", "bugi", "bong den", "dau nhot"],
        "service_package": ["goi", "500.000", "800.000", "1.200.000"],
        "basic_service_package": ["goi co ban", "500.000", "kiem tra tong the", "thay nhot"],
        "advanced_service_package": ["goi nang cao", "800.000", "bugi", "day curoa", "ac quy"],
        "premium_service_package": ["goi cao cap", "1.200.000", "loc nhien lieu", "he thong treo"],
        "maintenance_schedule": ["500km", "3,000km", "6,000km", "12,000km", "dinh ky", "ro-dai"],
        "break_in_service": ["ro-dai", "500km dau", "thay nhot lan dau"],
        "fee": ["phi", "mien phi", "66.000", "100.000", "200.000", "500.000", "1.500.000", "2-4 trieu"],
        "registration_time": ["bam bien", "ca vet", "1-3 ngay", "7-10 ngay"],
        "required_vehicle_documents": ["ca vet", "giay dang ky", "kiem dinh", "tnds bat buoc"],
        "plate_service": ["bao bien", "lam giay to tron goi", "ct07", "vneid"],
        "registration_process": ["tu dang ky", "to khai", "nop le phi", "nhan bien"],
        "return_boundary": ["chua xuat hoa don", "da xuat hoa don", "lan banh", "xe cu"],
        "return_conditions": ["nguyen tem", "niem phong", "phu kien", "qua tang"],
        "return_process": ["mang xe", "ktv kiem tra", "xac nhan", "hoan tien", "doi xe"],
        "depreciation": ["khau hao", "10-20%"],
        "delivery_area": ["noi thanh", "ngoai thanh", "khu vuc", "tinh lan can"],
        "delivery_fee": ["phi giao", "mien phi", "100.000", "200.000", "tinh theo km"],
        "delivery_time": ["1-2 gio", "2-4 gio", "1-2 ngay", "2-3 ngay"],
        "delivery_risk": ["rui ro van chuyen", "chiu 100%", "ky nhan"],
        "delivery_conditions": ["thanh toan du", "kiem dinh", "co mat nhan xe"],
        "delivery_inspection": ["no may", "kiem tra ngoai quan", "truoc khi nhan"],
        "compulsory_insurance": ["tnds", "bat buoc", "66.000", "nguoi bi tong"],
        "voluntary_insurance": ["tu nguyen", "mat cap", "toan dien", "1-1.5%"],
        "theft_insurance": ["mat cap", "chia goc", "ho so cong an", "70-80%"],
        "accident_insurance": ["tai nan", "20.000"],
        "current_promotion": ["khuyen mai hien tai", "thang 4", "giam 5%", "qua 500.000"],
        "student_promotion": ["sinh vien", "the sv", "giay bao trung tuyen", "balo"],
        "promotion_conditions": ["dieu kien ap dung", "qua hien vat", "khong quy doi"],
        "tradein_valuation": ["dinh gia", "15 phut", "kiem tra xe cu"],
        "tradein_documents": ["chinh chu", "hop dong mua ban", "uy quyen", "so khung", "so may"],
        "tradein_voucher": ["tro gia", "voucher", "1.000.000", "2.000.000"],
        "tradein_payment": ["chenh lech", "tra gop phan chenh lech"],
        "payment_methods": ["tien mat", "chuyen khoan", "atm", "visa", "mastercard", "jcb"],
        "credit_card_fee": ["phi quet the", "the tin dung", "1.5%", "2.5%"],
        "online_purchase": ["mua xe online", "video call", "so khung", "so may", "thanh toan phan con lai"],
        "testride_models": ["dong xe co san lai thu", "vario", "exciter", "winner"],
        "testride_conditions": ["bang lai", "a1", "a2", "cmnd", "cccd"],
        "testride_process": ["dat lich", "xuat trinh", "ky bien ban", "sa hinh"],
        "smart_app": ["my honda", "y-connect", "bao hanh dien tu", "nhac lich bao duong", "bluetooth"],
        "rescue_cases": ["thung lop", "chet may", "mat chia khoa", "het xang"],
        "rescue_fee": ["phi cuu ho", "mien phi", "10km", "bao gia truoc"],
        "fuel_consumption_faq": ["hao xang", "ton xang", "lit/100km", "fi"],
        "nationwide_warranty": ["bao hanh toan quoc", "ve que", "head", "yamaha town"],
        "plate_fee_reason": ["phi bien so", "moi noi mot gia", "phan vung", "ha noi", "tp.hcm"],
        "stock_availability": ["co san", "giao ngay", "check kho", "mau dac biet"],
        "fengshui_color": ["phong thuy", "menh", "mau xe", "hop mau", "kim", "moc", "thuy", "hoa", "tho"],
        "refund": ["hoan", "hoan coc", "doi tra"],
    }

    priority_slots = [
        "zero_interest",
        "deposit_amount",
        "plate_fee",
        "delivery_fee",
        "credit_card_fee",
        "rescue_fee",
        "warranty_engine",
        "warranty_frame",
        "warranty_parts",
        "warranty_conditions",
        "basic_service_package",
        "advanced_service_package",
        "premium_service_package",
        "break_in_service",
        "required_vehicle_documents",
        "plate_service",
        "registration_process",
        "delivery_time",
        "delivery_risk",
        "delivery_conditions",
        "delivery_inspection",
    ]

    for slot in priority_slots:
        if any(k in lower for k in slots.get(slot, [])):
            return slot

    for slot, keywords in slots.items():
        if any(k in lower for k in keywords):
            return slot

    return "policy" if domain != "general" else "general"


def _infer_brand(text: str) -> str:
    lower = _normalize_text(text)
    for brand in ["honda", "yamaha", "suzuki", "piaggio", "sym"]:
        if re.search(rf"(?<![a-z0-9]){brand}(?![a-z0-9])", lower):
            return brand
    return ""


def _is_policy_or_service_block(block: str) -> bool:
    return _infer_domain(block) != "general"


def _extract_structured_fact_chunks(block: str, section: str) -> List[Dict[str, Any]]:
    if not _is_policy_or_service_block(block):
        return []

    chunks: List[Dict[str, Any]] = []
    current_domain = _infer_domain(block)
    current_brand = ""
    heading = ""

    for raw_line in block.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("---"):
            continue

        if line.startswith("[") and line.endswith("]"):
            heading = line.strip("[]")
            inferred = _infer_domain(heading)
            if inferred != "general":
                current_domain = inferred
            continue

        normalized = _normalize_text(line)
        line_brand = _infer_brand(line)
        if line_brand:
            current_brand = line_brand

        line_domain = _infer_domain(line)
        domain = line_domain if line_domain != "general" else current_domain
        slot = _infer_slot(line, domain)

        is_fact = (
            line.startswith("-") or
            re.match(r"^\d+\.", line) or
            ":" in line or
            any(char.isdigit() for char in line)
        )
        if not is_fact:
            continue

        value = re.sub(r"^\s*[-+*]\s*", "", line)
        value = re.sub(r"^\s*\d+\.\s*", "", value).strip()
        if len(value) < 6:
            continue

        fact_text = (
            f"domain: {domain}\n"
            f"slot: {slot}\n"
            f"brand: {line_brand or current_brand}\n"
            f"value: {value}\n"
            f"source: {heading or section}"
        )
        chunks.append({
            "text": fact_text,
            "section": section,
            "product_name": "",
            "domain": domain,
            "slot": slot,
            "brand": line_brand or current_brand,
        })

    return chunks


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

        def add_chunk(chunk_text: str, metadata: Dict[str, Any] | None = None):
            nonlocal chunk_id
            if not chunk_text.strip():
                return
            metadata = metadata or {}
            chunks.append({
                "id": f"chunk_{chunk_id}",
                "text": chunk_text.strip(),
                "section": metadata.get("section", section),
                "product_name": metadata.get("product_name", product_name or ""),
                "domain": metadata.get("domain", ""),
                "slot": metadata.get("slot", ""),
                "brand": metadata.get("brand", ""),
            })
            chunk_id += 1

        fact_chunks = _extract_structured_fact_chunks(block, section)
        if fact_chunks:
            for fact in fact_chunks:
                add_chunk(fact["text"], fact)
            return

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
        try:
            sample = collection.get(limit=1, include=["metadatas"])
            metadata = (sample.get("metadatas") or [{}])[0] or {}
            has_structured_metadata = (
                "domain" in metadata and
                "slot" in metadata and
                "brand" in metadata and
                metadata.get("schema_version") == RAG_SCHEMA_VERSION
            )
        except Exception:
            has_structured_metadata = False

        if has_structured_metadata:
            print(f"[RAG] Index đã có {collection.count()} chunks")
            return collection.count()

        print("[RAG] Existing index is old schema. Rebuilding structured fact index...")
        try:
            existing = collection.get()
            if existing["ids"]:
                collection.delete(ids=existing["ids"])
        except Exception:
            pass

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
            "product_name": chunk["product_name"],
            "domain": chunk.get("domain", ""),
            "slot": chunk.get("slot", ""),
            "brand": chunk.get("brand", ""),
            "schema_version": RAG_SCHEMA_VERSION,
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
def _normalize_analysis_payload(analysis: Dict[str, Any] | None) -> Dict[str, Any]:
    raw = analysis or {}

    brand_preference = [
        str(x).strip() for x in raw.get("brand_preference", []) if str(x).strip()
    ]
    negative_preference = [
        str(x).strip() for x in raw.get("negative_preference", []) if str(x).strip()
    ]

    deduped_brand = list(dict.fromkeys(brand_preference))
    deduped_negative = list(dict.fromkeys(negative_preference))
    negative_set = {_normalize_text(x) for x in deduped_negative}

    deduped_brand = [
        x for x in deduped_brand if _normalize_text(x) not in negative_set
    ]

    budget_raw = raw.get("budget", {}) or {}
    budget = {
        "min": budget_raw.get("min"),
        "max": budget_raw.get("max"),
        "target": budget_raw.get("target"),
        "currency": budget_raw.get("currency") or "VND",
    }

    return {
        "intent": str(raw.get("intent") or "other").strip().lower(),
        "need": str(raw.get("need") or "").strip(),
        "brand_preference": deduped_brand,
        "negative_preference": deduped_negative,
        "budget": budget,
    }


def _build_semantic_query_variants(query: str, analysis: Dict[str, Any]) -> List[str]:
    variants = [query.strip()]

    semantic_parts: List[str] = []
    if analysis.get("intent"):
        semantic_parts.append(f"intent: {analysis['intent']}")
    if analysis.get("need"):
        semantic_parts.append(f"nhu cầu: {analysis['need']}")

    brands = analysis.get("brand_preference", [])
    if brands:
        semantic_parts.append("ưu tiên hãng: " + ", ".join(brands))

    negatives = analysis.get("negative_preference", [])
    if negatives:
        semantic_parts.append("loại trừ: " + ", ".join(negatives))

    budget = analysis.get("budget", {})
    if budget.get("min") is not None or budget.get("max") is not None or budget.get("target") is not None:
        semantic_parts.append(
            "ngân sách VND min={min} max={max} target={target}".format(
                min=budget.get("min"),
                max=budget.get("max"),
                target=budget.get("target"),
            )
        )

    if semantic_parts:
        variants.append(" ; ".join(semantic_parts))

    return list(dict.fromkeys([v for v in variants if v]))


def _extract_brand_from_product_name(product_name: str) -> str:
    name = (product_name or "").strip()
    if not name:
        return ""
    return name.split()[0]


def _doc_matches_negative_preference(doc: str, product_name: str, negatives: List[str]) -> bool:
    if not negatives:
        return False

    normalized_doc = _normalize_text(f"{product_name} {doc}")
    for item in negatives:
        token = _normalize_text(item)
        if not token:
            continue
        if re.search(rf"(?<![a-z0-9]){re.escape(token)}(?![a-z0-9])", normalized_doc):
            return True

    return False


def _score_doc_by_semantic_profile(section: str, product_name: str, analysis: Dict[str, Any]) -> float:
    bonus = 0.0
    intent = analysis.get("intent", "")

    if intent in {"recommendation", "comparison"}:
        if section == "comparison":
            bonus += 0.12
        elif section == "advisory":
            bonus += 0.10
        elif section == "general_advisory":
            bonus += 0.05

    preferred_brands = {_normalize_text(x) for x in analysis.get("brand_preference", [])}
    product_brand = _normalize_text(_extract_brand_from_product_name(product_name))
    if preferred_brands and product_brand and product_brand in preferred_brands:
        bonus += 0.16

    return bonus


def _score_doc_by_structured_metadata(meta: Dict[str, Any], query: str) -> float:
    normalized_query = _normalize_text(query)
    bonus = 0.0

    query_domain = _infer_domain(query)
    query_slot = _infer_slot(query, query_domain)
    query_brand = _infer_brand(query)

    doc_domain = str(meta.get("domain", "") or "")
    doc_slot = str(meta.get("slot", "") or "")
    doc_brand = str(meta.get("brand", "") or "")

    if query_domain != "general" and doc_domain == query_domain:
        bonus += 0.35

    if query_slot != "general" and doc_slot == query_slot:
        bonus += 0.30

    if query_brand and doc_brand and _normalize_text(query_brand) == _normalize_text(doc_brand):
        bonus += 0.25

    if doc_domain and doc_domain in normalized_query:
        bonus += 0.08

    if doc_slot and doc_slot in normalized_query:
        bonus += 0.08

    return bonus


# -------------------------------------------------------
# SEARCH
# -------------------------------------------------------
def search(query: str, top_k: int = TOP_K, analysis: Dict[str, Any] | None = None):
    collection = _get_collection()
    top_k = max(1, int(top_k))
    semantic_analysis = _normalize_analysis_payload(analysis)

    if collection.count() == 0:
        build_index()

    count = collection.count()
    if count == 0:
        return "(Không tìm thấy dữ liệu)"

    variants = _build_semantic_query_variants(query, semantic_analysis)
    negatives = semantic_analysis.get("negative_preference", [])

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

            if _doc_matches_negative_preference(doc, product_name, negatives):
                continue

            score += _score_doc_by_semantic_profile(section, product_name, semantic_analysis)
            score += _score_doc_by_structured_metadata(meta, q)

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
