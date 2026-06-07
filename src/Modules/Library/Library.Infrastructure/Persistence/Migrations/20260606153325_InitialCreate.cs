using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Library.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StudentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookTitle = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    BorrowedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReturnedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FineAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_StudentId",
                table: "Loans",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loans");
        }
    }
}
