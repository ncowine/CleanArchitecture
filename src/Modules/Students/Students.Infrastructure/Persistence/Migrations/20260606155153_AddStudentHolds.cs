using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Students.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StudentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PlacedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentHolds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentHolds_StudentId",
                table: "StudentHolds",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentHolds");
        }
    }
}
