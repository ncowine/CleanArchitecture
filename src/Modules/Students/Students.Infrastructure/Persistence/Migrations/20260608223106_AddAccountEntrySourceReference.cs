using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Students.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountEntrySourceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceReference",
                table: "AccountEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountEntries_SourceReference",
                table: "AccountEntries",
                column: "SourceReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountEntries_SourceReference",
                table: "AccountEntries");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "AccountEntries");
        }
    }
}
