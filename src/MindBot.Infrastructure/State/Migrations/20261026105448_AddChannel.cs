using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindBot.Infrastructure.State.Migrations;

/// <inheritdoc />
public partial class AddChannel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_ProcessedUpdates",
            table: "ProcessedUpdates");

        migrationBuilder.DropPrimaryKey(
            name: "PK_Conversations",
            table: "Conversations");

        migrationBuilder.AddColumn<int>(
            name: "Channel",
            table: "WriteJobs",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "Channel",
            table: "ProcessedUpdates",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "Channel",
            table: "Conversations",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "Channel",
            table: "BackgroundJobs",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddPrimaryKey(
            name: "PK_ProcessedUpdates",
            table: "ProcessedUpdates",
            columns: new[] { "Channel", "UpdateId" });

        migrationBuilder.AddPrimaryKey(
            name: "PK_Conversations",
            table: "Conversations",
            columns: new[] { "Channel", "ChatId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_ProcessedUpdates",
            table: "ProcessedUpdates");

        migrationBuilder.DropPrimaryKey(
            name: "PK_Conversations",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "Channel",
            table: "WriteJobs");

        migrationBuilder.DropColumn(
            name: "Channel",
            table: "ProcessedUpdates");

        migrationBuilder.DropColumn(
            name: "Channel",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "Channel",
            table: "BackgroundJobs");

        migrationBuilder.AddPrimaryKey(
            name: "PK_ProcessedUpdates",
            table: "ProcessedUpdates",
            column: "UpdateId");

        migrationBuilder.AddPrimaryKey(
            name: "PK_Conversations",
            table: "Conversations",
            column: "ChatId");
    }
}
