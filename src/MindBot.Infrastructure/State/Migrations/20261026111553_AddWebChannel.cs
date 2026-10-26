using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindBot.Infrastructure.State.Migrations;

/// <inheritdoc />
public partial class AddWebChannel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeviceTokens",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceTokens", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WebMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ClientMessageId = table.Column<string>(type: "TEXT", nullable: true),
                Direction = table.Column<int>(type: "INTEGER", nullable: false),
                Text = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                InReplyToId = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WebMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeviceTokens_Name",
            table: "DeviceTokens",
            column: "Name",
            unique: true,
            filter: "\"RevokedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_DeviceTokens_TokenHash",
            table: "DeviceTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WebMessages_ClientMessageId",
            table: "WebMessages",
            column: "ClientMessageId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WebMessages_InReplyToId",
            table: "WebMessages",
            column: "InReplyToId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceTokens");

        migrationBuilder.DropTable(
            name: "WebMessages");
    }
}
