using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIAP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSuperAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("INSERT INTO \"Roles\" (\"Id\", \"Name\") VALUES (2, 'admin') ON CONFLICT (\"Id\") DO NOTHING;");
            migrationBuilder.Sql("UPDATE \"Users\" SET \"RoleId\" = 2 WHERE \"RoleId\" = 4;");

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "RoleId",
                value: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name" },
                values: new object[] { 4, "super-admin" });

            migrationBuilder.Sql("UPDATE \"Users\" SET \"RoleId\" = 4 WHERE \"RoleId\" = 2 AND \"Id\" = '00000000-0000-0000-0000-000000000002';");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "RoleId",
                value: 4);
        }
    }
}
