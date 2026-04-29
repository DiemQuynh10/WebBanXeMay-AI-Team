using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chatbot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationSessionId = table.Column<int>(type: "int", nullable: false),
                    CurrentDomain = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrentGoalType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrentGoalStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastIntentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastQuestionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastBotQuestionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastResolvedReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConstraintsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CandidateProductIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MentionedProductIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MentionedProductNamesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TurnSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CarryForwardConfidence = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsAwaitingClarification = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationStates_ConversationSessions_ConversationSessionId",
                        column: x => x.ConversationSessionId,
                        principalTable: "ConversationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_ConversationSessionId",
                table: "ConversationStates",
                column: "ConversationSessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationStates");
        }
    }
}
