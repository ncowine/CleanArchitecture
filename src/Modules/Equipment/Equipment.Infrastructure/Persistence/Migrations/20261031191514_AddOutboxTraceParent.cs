using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equipment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "OutboxMessages",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "OutboxMessages");
        }
    }
}
