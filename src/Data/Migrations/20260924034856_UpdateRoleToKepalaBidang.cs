using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIAP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRoleToKepalaBidang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "kepala bidang");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "kepala bagian");
        }
    }
}
