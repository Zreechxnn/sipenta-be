using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SIAP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBidangTableAndRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bidang",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Bidang",
                table: "Documents");

            migrationBuilder.AddColumn<int>(
                name: "BidangId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidangId",
                table: "Documents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Bidangs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nama = table.Column<string>(type: "text", nullable: false),
                    Kode = table.Column<string>(type: "text", nullable: true),
                    Deskripsi = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bidangs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Bidangs",
                columns: new[] { "Id", "CreatedAt", "Deskripsi", "Kode", "Nama", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Aplikasi Informatika", "APTIKA", "Bidang APTIKA", null },
                    { 2, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Teknologi Informasi dan Komunikasi", "TIK", "Bidang TIK", null },
                    { 3, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Informasi dan Komunikasi Publik", "IKP", "Bidang IKP", null },
                    { 4, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Statistik Sektoral", "STATISTIK", "Bidang Statistik", null },
                    { 5, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Persandian dan Keamanan Informasi", "SANIKAMI", "Bidang Persandian dan Keamanan Informasi", null },
                    { 6, new DateTime(2023, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sekretariat Diskominfo", "SEKRETARIAT", "Sekretariat", null }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "BidangId",
                value: 6);

            migrationBuilder.CreateIndex(
                name: "IX_Users_BidangId",
                table: "Users",
                column: "BidangId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BidangId",
                table: "Documents",
                column: "BidangId");

            migrationBuilder.CreateIndex(
                name: "IX_Bidangs_Nama",
                table: "Bidangs",
                column: "Nama",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Bidangs_BidangId",
                table: "Documents",
                column: "BidangId",
                principalTable: "Bidangs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Bidangs_BidangId",
                table: "Users",
                column: "BidangId",
                principalTable: "Bidangs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Bidangs_BidangId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Bidangs_BidangId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Bidangs");

            migrationBuilder.DropIndex(
                name: "IX_Users_BidangId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Documents_BidangId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "BidangId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BidangId",
                table: "Documents");

            migrationBuilder.AddColumn<string>(
                name: "Bidang",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bidang",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "Bidang",
                value: "Sekretariat");
        }
    }
}
