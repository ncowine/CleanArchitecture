using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RequiredEquipmentCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RequiredLicenceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RequiredAccessLevel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EquipmentStepStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EquipmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LicenceStepStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LicenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccessStepStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccessId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingRequests", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingRequests");
        }
    }
}
