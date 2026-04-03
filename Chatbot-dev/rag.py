"""
RAG Module - Retrieval-Augmented Generation
Đọc knowledge_base.txt, chia nhỏ thành chunks, lưu vector vào ChromaDB.
"""

import os
import re
from pathlib import Path
from openai import OpenAI
import chromadb

# -------------------------------------------------------
# CONFIG
# -------------------------------------------------------
KB_FILE = Path(__file__).parent / "knowledge_base.txt"
CHROMA_DIR = Path(__file__).parent / "chroma_db"
COLLECTION = "knowledge"
EMBED_MODEL = "text-embedding-3-small"
TOP_K = 4

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
# CHUNKING THÔNG MINH
# -------------------------------------------------------
def _split_into_chunks(text: str, chunk_size: int = 500) -> list[dict]:
    """
    Ưu tiên chia theo từng sản phẩm [TenXe] để mỗi chunk chứa
    thông tin đầy đủ của 1 sản phẩm.
    Nếu không có cấu trúc [..], fallback về cắt theo dòng trống.
    """
    chunks = []
    chunk_id = 0
    text = text.strip()
    if not text:
        return []

    # Thử tách theo block sản phẩm bắt đầu bằng [tên xe]
    # Pattern: dòng bắt đầu bằng [ và kết thúc bằng ]
    product_blocks = re.split(r'(?=^\[)', text, flags=re.MULTILINE)

    # Lấy phần header (trước block đầu tiên) làm 1 chunk riêng
    for block in product_blocks:
        block = block.strip()
        if not block:
            continue

        if len(block) <= chunk_size:
            # Chunk vừa đủ → giữ nguyên
            chunks.append({"id": f"chunk_{chunk_id}", "text": block})
            chunk_id += 1
        else:
            # Chunk quá lớn → chia tiếp theo dòng trống
            paragraphs = re.split(r'\n{2,}', block)
            buffer = ""
            for para in paragraphs:
                if len(buffer) + len(para) < chunk_size:
                    buffer += ("\n\n" if buffer else "") + para
                else:
                    if buffer:
                        chunks.append({"id": f"chunk_{chunk_id}", "text": buffer.strip()})
                        chunk_id += 1
                    buffer = para
            if buffer.strip():
                chunks.append({"id": f"chunk_{chunk_id}", "text": buffer.strip()})
                chunk_id += 1

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
        except:
            pass

    if not force and collection.count() > 0:
        print(f"[RAG] Index đã có {collection.count()} chunks")
        return collection.count()

    if not KB_FILE.exists():
        raise FileNotFoundError(f"Không tìm thấy {KB_FILE}")

    text = KB_FILE.read_text(encoding="utf-8")

    chunks = _split_into_chunks(text)

    print(f"[RAG] Đang index {len(chunks)} chunks...")

    if len(chunks) == 0:
        raise ValueError("knowledge_base.txt không có nội dung hợp lệ")

    ids = []
    texts = []
    embeddings = []

    for chunk in chunks:

        print("→", chunk["text"][:80].replace("\n", " "), "...")

        emb = _embed(chunk["text"])

        ids.append(chunk["id"])
        texts.append(chunk["text"])
        embeddings.append(emb)

    collection.add(
        ids=ids,
        documents=texts,
        embeddings=embeddings
    )

    print(f"[RAG] ✅ Đã index {len(ids)} chunks")

    return len(ids)


# -------------------------------------------------------
# SEARCH
# -------------------------------------------------------
def search(query: str, top_k: int = TOP_K):

    collection = _get_collection()

    if collection.count() == 0:
        build_index()

    query_embedding = _embed(query)

    results = collection.query(
        query_embeddings=[query_embedding],
        n_results=min(top_k, collection.count()),
        include=["documents", "distances"]
    )

    docs = results["documents"][0]
    distances = results["distances"][0]  # cosine distance (thấp = liên quan hơn)

    if not docs:
        return "(Không tìm thấy dữ liệu)"

    # Chỉ giữ các chunk có độ liên quan đủ cao (distance < 0.6)
    filtered = [
        doc for doc, dist in zip(docs, distances)
        if dist < 0.6
    ]

    # Nếu lọc quá chặt không còn gì, lấy chunk tốt nhất
    if not filtered:
        filtered = [docs[0]]

    return "\n\n---\n\n".join(filtered)


# -------------------------------------------------------
# 5. Chạy thử trực tiếp
# -------------------------------------------------------
if __name__ == "__main__":
    from dotenv import load_dotenv
    load_dotenv()

    print("=== BUILD INDEX ===")
    n = build_index(force=True)
    print(f"Tổng số chunks: {n}\n")

    print("=== THỬ TÌM KIẾM ===")
    queries = [
        "Honda SH giá bao nhiêu?",
        "Xe nào tiết kiệm xăng nhất?",
        "Chính sách trả góp như thế nào?",
        "Giờ làm việc của cửa hàng?",
    ]
    for q in queries:
        print(f"\n❓ {q}")
        print(search(q, top_k=2))
        print("-" * 50)
