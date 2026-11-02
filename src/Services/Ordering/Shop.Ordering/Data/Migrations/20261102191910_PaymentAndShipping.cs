using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Ordering.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentAndShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "card_last4",
                table: "orders",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "refunded",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tracking_code",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_deadline",
                table: "order_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_token",
                table: "order_states",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_order_states_current_state_payment_deadline",
                table: "order_states",
                columns: new[] { "current_state", "payment_deadline" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_states_current_state_payment_deadline",
                table: "order_states");

            migrationBuilder.DropColumn(
                name: "card_last4",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "refunded",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_deadline",
                table: "order_states");

            migrationBuilder.DropColumn(
                name: "payment_token",
                table: "order_states");
        }
    }
}
