import pyodbc

server = r"HUYENPEA"
database = "WebBanXeMay"

conn_str = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    f"SERVER={server};"
    f"DATABASE={database};"
    "Trusted_Connection=yes;"
    "TrustServerCertificate=yes;"
)

print(conn_str)

conn = pyodbc.connect(conn_str, timeout=10)
print("Kết nối OK")
conn.close()