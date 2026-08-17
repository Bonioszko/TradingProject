using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaperTrade.Trading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3AsynchronousExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "http_idempotency_requests",
                schema: "trading",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_idempotency_requests", x => x.Key);
                    table.ForeignKey(
                        name: "FK_http_idempotency_requests_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "trading",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "trading",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "trading",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractVersion = table.Column<int>(type: "integer", nullable: false),
                    Exchange = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoutingKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_http_idempotency_requests_OrderId",
                schema: "trading",
                table: "http_idempotency_requests",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_Consumer_ProcessedAt",
                schema: "trading",
                table: "inbox_messages",
                columns: new[] { "Consumer", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_AggregateId",
                schema: "trading",
                table: "outbox_messages",
                column: "AggregateId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_OccurredAt",
                schema: "trading",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "http_idempotency_requests",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "trading");
        }
    }
}
