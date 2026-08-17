using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaperTrade.Trading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4ReliabilityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_PublishedAt_OccurredAt",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                schema: "trading",
                table: "outbox_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "trading",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                schema: "trading",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "trading",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                schema: "trading",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_SuspendedAt_NextAttemptAt_Lease~",
                schema: "trading",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "SuspendedAt", "NextAttemptAt", "LeaseExpiresAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_PublishedAt_SuspendedAt_NextAttemptAt_Lease~",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                schema: "trading",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_OccurredAt",
                schema: "trading",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });
        }
    }
}
