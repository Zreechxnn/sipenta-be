using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIAP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRoleKasubagToKepalaBagian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SystemSettings"" (
                    ""Key"" text NOT NULL,
                    ""Value"" text NOT NULL,
                    ""Description"" text NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedBy"" text NULL,
                    CONSTRAINT ""PK_SystemSettings"" PRIMARY KEY (""Key"")
                );
            ");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "kepala bagian");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "kasubag");
        }
    }
}
