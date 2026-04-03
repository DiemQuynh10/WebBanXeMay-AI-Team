"""
db_to_kb.py
-----------
Kết nối SQL Server (WebBanXeMay), lấy dữ liệu sản phẩm + loại + thương hiệu + đánh giá,
ghi ra knowledge_base.txt rồi tự động build lại RAG index.

Hỗ trợ:
- Windows Authentication (mặc định nếu DB_USER để trống)
- SQL Server Authentication (nếu có DB_USER/DB_PASSWORD)

Cách chạy:
    python db_to_kb.py
"""

from __future__ import annotations

import os
import sys
import pyodbc
from pathlib import Path
from typing import Any
from dotenv import load_dotenv


load_dotenv(dotenv_path=Path(__file__).resolve().parent / ".env", override=True)

# =======================================================
# CẤU HÌNH
# =======================================================
DB_SERVER = os.environ.get("DB_SERVER", r"LAPTOP-IUA333SC\TESTDB").strip()
DB_NAME = os.environ.get("DB_NAME", "WebBanXeMay").strip()
DB_USER = os.environ.get("DB_USER", "").strip()          # để trống => Windows Auth
DB_PASSWORD = os.environ.get("DB_PASSWORD", "").strip()

BASE_DIR = Path(__file__).resolve().parent
OUTPUT_FILE = BASE_DIR / "knowledge_base.txt"

print("DEBUG DB_SERVER =", repr(DB_SERVER))
print("DEBUG DB_NAME =", repr(DB_NAME))
print("DEBUG DB_USER =", repr(DB_USER))
print("DEBUG DB_PASSWORD empty =", DB_PASSWORD == "")
# =======================================================
# KẾT NỐI SQL SERVER
# =======================================================
def get_available_driver() -> str:
    preferred_drivers = [
        "ODBC Driver 18 for SQL Server",
        "ODBC Driver 17 for SQL Server",
        "ODBC Driver 13 for SQL Server",
        "SQL Server Native Client 11.0",
        "SQL Server",
    ]

    installed = pyodbc.drivers()
    if not installed:
        raise RuntimeError("Không tìm thấy ODBC driver cho SQL Server trên máy.")

    for driver in preferred_drivers:
        if driver in installed:
            return driver

    return installed[0]


def build_connection_string(driver: str) -> str:
    if DB_USER:
        # SQL Server Authentication
        return (
            f"DRIVER={{{driver}}};"
            f"SERVER={DB_SERVER};"
            f"DATABASE={DB_NAME};"
            f"UID={DB_USER};"
            f"PWD={DB_PASSWORD};"
            "TrustServerCertificate=yes;"
        )

    # Windows Authentication
    return (
        f"DRIVER={{{driver}}};"
        f"SERVER={DB_SERVER};"
        f"DATABASE={DB_NAME};"
        "Trusted_Connection=yes;"
        "TrustServerCertificate=yes;"
    )


def get_connection() -> pyodbc.Connection:
    driver = get_available_driver()
    print(f"[DB] Dùng driver: {driver}")

    conn_str = build_connection_string(driver)
    return pyodbc.connect(conn_str, timeout=10)


# =======================================================
# TIỆN ÍCH
# =======================================================
def rows_to_dicts(cursor: pyodbc.Cursor) -> list[dict[str, Any]]:
    columns = [col[0] for col in cursor.description]
    return [dict(zip(columns, row)) for row in cursor.fetchall()]


def format_gia(gia: Any) -> str:
    if gia is None:
        return "Liên hệ"
    try:
        return f"{int(gia):,} đ".replace(",", ".")
    except Exception:
        return str(gia)


def clean_text(value: Any, max_length: int | None = None) -> str:
    if value is None:
        return ""
    text = str(value).strip()
    if not text:
        return ""
    text = " ".join(text.split())
    if max_length and len(text) > max_length:
        return text[: max_length - 3] + "..."
    return text


# =======================================================
# QUERY DỮ LIỆU
# =======================================================
def fetch_products(cursor: pyodbc.Cursor) -> list[dict[str, Any]]:
    """
    Lấy sản phẩm JOIN với Loại, Thương hiệu (nếu có) và tổng hợp đánh giá.
    Thử query có thương hiệu trước; nếu schema khác thì fallback query không có thương hiệu.
    """
    query_with_brand = """
        SELECT
            sp.MaSP,
            sp.TenSP,
            sp.Gia,
            sp.SoLuong,
            sp.CC,
            sp.MoTa,
            sp.IsActive,
            l.TenLoai,
            th.TenTH AS TenThuongHieu,
            ROUND(AVG(CAST(dg.DiemDanhGia AS FLOAT)), 1) AS DiemTrungBinh,
            COUNT(dg.MaDanhGia)                          AS SoDanhGia
        FROM [dbo].[SanPhams] sp
        LEFT JOIN [dbo].[Loais]       l  ON sp.MaLoai = l.MaLoai
        LEFT JOIN [dbo].[ThuongHieus] th ON sp.MaTH   = th.MaTH
        LEFT JOIN [dbo].[DanhGias]    dg ON sp.MaSP   = dg.MaSP
                                         AND dg.IsApproved = 1
        WHERE sp.IsActive = 1
        GROUP BY
            sp.MaSP, sp.TenSP, sp.Gia, sp.SoLuong,
            sp.CC, sp.MoTa, sp.IsActive, l.TenLoai, th.TenTH
        ORDER BY l.TenLoai, sp.TenSP
    """

    query_without_brand = """
        SELECT
            sp.MaSP,
            sp.TenSP,
            sp.Gia,
            sp.SoLuong,
            sp.CC,
            sp.MoTa,
            sp.IsActive,
            l.TenLoai,
            NULL AS TenThuongHieu,
            ROUND(AVG(CAST(dg.DiemDanhGia AS FLOAT)), 1) AS DiemTrungBinh,
            COUNT(dg.MaDanhGia)                          AS SoDanhGia
        FROM [dbo].[SanPhams] sp
        LEFT JOIN [dbo].[Loais]    l  ON sp.MaLoai = l.MaLoai
        LEFT JOIN [dbo].[DanhGias] dg ON sp.MaSP   = dg.MaSP
                                      AND dg.IsApproved = 1
        WHERE sp.IsActive = 1
        GROUP BY
            sp.MaSP, sp.TenSP, sp.Gia, sp.SoLuong,
            sp.CC, sp.MoTa, sp.IsActive, l.TenLoai
        ORDER BY l.TenLoai, sp.TenSP
    """

    try:
        cursor.execute(query_with_brand)
        return rows_to_dicts(cursor)
    except Exception:
        cursor.execute(query_without_brand)
        return rows_to_dicts(cursor)


def fetch_top_reviews(cursor: pyodbc.Cursor, ma_sp: int, limit: int = 3) -> list[str]:
    """
    Lấy tối đa N đánh giá nổi bật của sản phẩm.
    Nếu DB không có cột IsFeatured thì fallback lấy đánh giá mới nhất đã duyệt.
    """
    query_featured = """
        SELECT TOP (?) TenNguoiDanhGia, NoiDung, DiemDanhGia
        FROM [dbo].[DanhGias]
        WHERE MaSP = ? AND IsApproved = 1 AND IsFeatured = 1
        ORDER BY NgayDanhGia DESC
    """

    query_latest = """
        SELECT TOP (?) TenNguoiDanhGia, NoiDung, DiemDanhGia
        FROM [dbo].[DanhGias]
        WHERE MaSP = ? AND IsApproved = 1
        ORDER BY NgayDanhGia DESC
    """

    try:
        cursor.execute(query_featured, limit, ma_sp)
        rows = cursor.fetchall()
    except Exception:
        cursor.execute(query_latest, limit, ma_sp)
        rows = cursor.fetchall()

    results: list[str] = []
    for row in rows:
        reviewer = clean_text(row[0]) or "Khách hàng"
        content = clean_text(row[1], max_length=160)
        rating = row[2]
        if content:
            results.append(f"  ★ {rating}/5 - {reviewer}: {content}")

    return results


# =======================================================
# TRI THỨC TƯ VẤN
# =======================================================
def build_general_consulting_knowledge() -> list[str]:
    return [
        "--- TRI THỨC TƯ VẤN CHUNG ---",
        "",
        "Xe ga phù hợp với người cần dễ lái, cốp tiện dụng, di chuyển trong đô thị.",
        "Xe số thường tiết kiệm xăng hơn và chi phí bảo dưỡng thấp hơn.",
        "Sinh viên thường ưu tiên xe bền, tiết kiệm xăng, giá hợp lý và dễ sửa chữa.",
        "Người dùng nữ thường ưu tiên xe nhẹ, dễ điều khiển, thiết kế gọn gàng và tiện dụng.",
        "Đi học trong thành phố thường ưu tiên xe dễ xoay trở, ít tốn nhiên liệu và còn hàng trong hệ thống.",
        "Nếu người dùng hỏi tư vấn xe, nên ưu tiên các mẫu phổ biến, dễ bảo trì, có phụ tùng phổ biến và còn hàng.",
        "Khi tư vấn xe ga cho sinh viên nữ, thường ưu tiên xe gọn nhẹ, dễ điều khiển, tiết kiệm nhiên liệu và giá hợp lý.",
        "Khi tư vấn xe số cho sinh viên, thường ưu tiên xe bền, tiết kiệm xăng và chi phí bảo dưỡng thấp.",
        "",
    ]


# =======================================================
# TẠO KNOWLEDGE BASE
# =======================================================
def build_knowledge_base(products: list[dict[str, Any]], cursor: pyodbc.Cursor) -> str:
    lines: list[str] = []

    # Nhóm theo loại xe
    groups: dict[str, list[dict[str, Any]]] = {}
    for p in products:
        loai = clean_text(p.get("TenLoai")) or "Khác"
        groups.setdefault(loai, []).append(p)

    lines.append("--- SẢN PHẨM XE MÁY ---")
    lines.append("")

    for loai, items in groups.items():
        lines.append(f"== {loai} ==")
        lines.append("")

        for p in items:
            ten_sp = clean_text(p.get("TenSP")) or "Không rõ tên sản phẩm"
            lines.append(f"[{ten_sp}]")

            if clean_text(p.get("TenThuongHieu")):
                lines.append(f"Thương hiệu: {clean_text(p.get('TenThuongHieu'))}")

            lines.append(f"Giá: {format_gia(p.get('Gia'))}")

            if p.get("CC"):
                lines.append(f"Động cơ: {clean_text(p.get('CC'))}cc")

            if p.get("SoLuong") is not None:
                ton = int(p["SoLuong"])
                tinh_trang = "Còn hàng" if ton > 0 else "Hết hàng"
                lines.append(f"Tình trạng: {tinh_trang} ({ton} xe)")

            mo_ta = clean_text(p.get("MoTa"), max_length=300)
            if mo_ta:
                lines.append(f"Mô tả: {mo_ta}")

            diem_tb = p.get("DiemTrungBinh")
            so_danh_gia = p.get("SoDanhGia")
            if diem_tb is not None and so_danh_gia:
                lines.append(f"Đánh giá: {diem_tb}/5 ({int(so_danh_gia)} lượt đánh giá)")

                reviews = fetch_top_reviews(cursor, int(p["MaSP"]), limit=3)
                if reviews:
                    lines.append("Đánh giá nổi bật:")
                    lines.extend(reviews)

            lines.append("")

    # Bổ sung tri thức tư vấn cho RAG
    lines.extend(build_general_consulting_knowledge())

    return "\n".join(lines)


# =======================================================
# MAIN
# =======================================================
def main() -> None:
    print("[DB] Đang kết nối SQL Server...")

    try:
        conn = get_connection()
    except Exception as ex:
        print(f"\n❌ Kết nối thất bại: {ex}")
        print("\n💡 Kiểm tra lại:")
        print("   1. SQL Server đang chạy chưa? (Services → SQL Server)")
        print(r"   2. Tên server đúng chưa? Ví dụ: LAPTOP-IUA333SC\TESTDB, localhost, .\SQLEXPRESS")
        print("   3. Nếu dùng Windows Authentication:")
        print("      - Để trống DB_USER và DB_PASSWORD trong file .env")
        print("      - Đảm bảo tài khoản Windows hiện tại có quyền truy cập database")
        print("   4. Nếu dùng SQL Server Authentication:")
        print("      DB_SERVER=ten_server")
        print("      DB_NAME=WebBanXeMay")
        print("      DB_USER=sa")
        print("      DB_PASSWORD=mat_khau")
        sys.exit(1)

    print("[DB] ✅ Kết nối thành công!")

    try:
        cursor = conn.cursor()

        print("[DB] Đang đọc dữ liệu sản phẩm...")
        products = fetch_products(cursor)
        print(f"[DB] → Tìm thấy {len(products)} sản phẩm đang hoạt động")

        if not products:
            print("⚠️ Không có sản phẩm nào (IsActive=1). Kiểm tra lại dữ liệu.")
            sys.exit(1)

        print("[DB] Đang tạo knowledge_base.txt...")
        kb_content = build_knowledge_base(products, cursor)

        OUTPUT_FILE.write_text(kb_content, encoding="utf-8")
        print(f"[DB] ✅ Đã ghi {OUTPUT_FILE} ({len(kb_content):,} ký tự)")

    finally:
        conn.close()

    print("\n[RAG] Đang build lại vector index...")
    try:
        import rag
        chunks = rag.build_index(force=True)
        print(f"[RAG] ✅ Đã index {chunks} chunks. Chatbot sẵn sàng!")
    except Exception as ex:
        print(f"[RAG] ❌ Build index thất bại: {ex}")
        sys.exit(1)

    print("\n👉 Chạy server:")
    print("   python -m uvicorn chatbot:app --reload")


if __name__ == "__main__":
    main()