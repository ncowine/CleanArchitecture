using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Library.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CopyBasedLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookTitle",
                table: "Loans");

            migrationBuilder.AddColumn<Guid>(
                name: "CopyId",
                table: "Loans",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "RenewalCount",
                table: "Loans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_CopyId",
                table: "Loans",
                column: "CopyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Loans_CopyId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "CopyId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "RenewalCount",
                table: "Loans");

            migrationBuilder.AddColumn<string>(
                name: "BookTitle",
                table: "Loans",
                type: "TEXT",
                maxLength: 300,
                nullable: false,
                defaultValue: "");
        }
    }
}
