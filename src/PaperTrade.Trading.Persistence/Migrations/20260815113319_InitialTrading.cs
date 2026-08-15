using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PaperTrade.Trading.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableCash = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MarketPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    LimitPrice = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_orders_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "trading",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalSchema: "trading",
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executions",
                schema: "trading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Commission = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_executions_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "trading",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "trading",
                table: "accounts",
                columns: new[] { "Id", "AvailableCash", "Currency", "Version" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000001"), 100000m, "USD", 0L });

            migrationBuilder.InsertData(
                schema: "trading",
                table: "instruments",
                columns: new[] { "Id", "Currency", "MarketPrice", "Name", "Symbol" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), "USD", 400m, "Microsoft Corporation", "MSFT" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), "USD", 200m, "Apple Inc.", "AAPL" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), "USD", 150m, "NVIDIA Corporation", "NVDA" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_executions_OrderId",
                schema: "trading",
                table: "executions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instruments_Symbol",
                schema: "trading",
                table: "instruments",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_AccountId",
                schema: "trading",
                table: "orders",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_InstrumentId",
                schema: "trading",
                table: "orders",
                column: "InstrumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "executions",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "instruments",
                schema: "trading");
        }
    }
}
