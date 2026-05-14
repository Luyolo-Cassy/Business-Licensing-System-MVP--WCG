using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedDocumentName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedDocumentName",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedDocumentName",
                table: "Applications");
        }
    }
}
