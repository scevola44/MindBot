
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindBot.Infrastructure.State.Migrations;

/// <inheritdoc />
public partial class AddBackgroundJobs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BackgroundJobs",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UpdateId = table.Column<long>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", nullable: false),
                Payload = table.Column<string>(type: "TEXT", nullable: false),
                ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                SenderId = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                LastError = table.Column<string>(type: "TEXT", nullable: true),
                EnqueuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BackgroundJobs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BackgroundJobs_Kind_Status_Id",
            table: "BackgroundJobs",
            columns: new[] { "Kind", "Status", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BackgroundJobs");
    }
}
