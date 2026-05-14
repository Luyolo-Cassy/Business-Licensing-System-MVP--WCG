using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedDocumentPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadedDocumentPath",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadedDocumentPath",
                table: "Applications");
        }
    }
}
