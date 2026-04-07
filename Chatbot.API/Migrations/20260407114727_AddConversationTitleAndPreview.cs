using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chatbot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTitleAndPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversationMessages_ConversationSessionId",
                table: "ConversationMessages");

            migrationBuilder.AddColumn<string>(
                name: "LastMessagePreview",
                table: "ConversationSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ConversationSessions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSessions_Channel_UserId_UpdatedAtUtc",
                table: "ConversationSessions",
                columns: new[] { "Channel", "UserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationSessionId_CreatedAtUtc",
                table: "ConversationMessages",
                columns: new[] { "ConversationSessionId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversationSessions_Channel_UserId_UpdatedAtUtc",
                table: "ConversationSessions");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMessages_ConversationSessionId_CreatedAtUtc",
                table: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "LastMessagePreview",
                table: "ConversationSessions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ConversationSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationSessionId",
                table: "ConversationMessages",
                column: "ConversationSessionId");
        }
    }
}
