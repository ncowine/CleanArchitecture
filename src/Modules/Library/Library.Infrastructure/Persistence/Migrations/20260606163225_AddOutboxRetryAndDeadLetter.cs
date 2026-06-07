using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Library.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryAndDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "OutboxMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetteredOnUtc",
                table: "OutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_DeadLetteredOnUtc",
                table: "OutboxMessages",
                columns: new[] { "ProcessedOnUtc", "DeadLetteredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_DeadLetteredOnUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "DeadLetteredOnUtc",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc",
                table: "OutboxMessages",
                column: "ProcessedOnUtc");
        }
    }
}
