using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoobackVash.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotobook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploaderUid = table.Column<string>(type: "text", nullable: false),
                    UploaderName = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    AltText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Moderation = table.Column<string>(type: "jsonb", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhotoImages",
                columns: table => new
                {
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Full = table.Column<byte[]>(type: "bytea", nullable: false),
                    Thumb = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoImages", x => x.PhotoId);
                    table.ForeignKey(
                        name: "FK_PhotoImages_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_Status_CreatedAt",
                table: "Photos",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoImages");

            migrationBuilder.DropTable(
                name: "Photos");
        }
    }
}
