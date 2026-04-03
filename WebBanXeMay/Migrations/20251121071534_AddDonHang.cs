using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebBanXeMay.Migrations
{
    /// <inheritdoc />
    public partial class AddDonHang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaVoucher",
                table: "DonHangs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SoTienGiam",
                table: "DonHangs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoucherId",
                table: "DonHangs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DonHangs_VoucherId",
                table: "DonHangs",
                column: "VoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_DonHangs_Vouchers_VoucherId",
                table: "DonHangs",
                column: "VoucherId",
                principalTable: "Vouchers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DonHangs_Vouchers_VoucherId",
                table: "DonHangs");

            migrationBuilder.DropIndex(
                name: "IX_DonHangs_VoucherId",
                table: "DonHangs");

            migrationBuilder.DropColumn(
                name: "MaVoucher",
                table: "DonHangs");

            migrationBuilder.DropColumn(
                name: "SoTienGiam",
                table: "DonHangs");

            migrationBuilder.DropColumn(
                name: "VoucherId",
                table: "DonHangs");
        }
    }
}
