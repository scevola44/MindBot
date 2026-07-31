using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindBot.Infrastructure.State.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Conversations",
            columns: table => new
            {
                ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                Stage = table.Column<int>(type: "INTEGER", nullable: false),
                PendingNoteName = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Conversations", x => x.ChatId);
            });

        migrationBuilder.CreateTable(
            name: "ProcessedUpdates",
            columns: table => new
            {
                UpdateId = table.Column<long>(type: "INTEGER", nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcessedUpdates", x => x.UpdateId);
            });

        migrationBuilder.CreateTable(
            name: "RepositoryState",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                LastPushedSha = table.Column<string>(type: "TEXT", nullable: true),
                LastTelegramOffset = table.Column<int>(type: "INTEGER", nullable: false),
                LastSuccessfulPushAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RepositoryState", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WriteJobs",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UpdateId = table.Column<long>(type: "INTEGER", nullable: false),
                Filename = table.Column<string>(type: "TEXT", nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                SenderId = table.Column<long>(type: "INTEGER", nullable: false),
                EnqueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WriteJobs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProcessedUpdates_ReceivedAt",
            table: "ProcessedUpdates",
            column: "ReceivedAt");

        migrationBuilder.CreateIndex(
            name: "IX_WriteJobs_Filename",
            table: "WriteJobs",
            column: "Filename");

        migrationBuilder.CreateIndex(
            name: "IX_WriteJobs_Status_Id",
            table: "WriteJobs",
            columns: ["Status", "Id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Conversations");
        migrationBuilder.DropTable(name: "ProcessedUpdates");
        migrationBuilder.DropTable(name: "RepositoryState");
        migrationBuilder.DropTable(name: "WriteJobs");
    }
}
