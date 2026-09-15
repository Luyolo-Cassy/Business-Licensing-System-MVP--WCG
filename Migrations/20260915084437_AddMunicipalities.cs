using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Municipalities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Municipalities", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Municipalities",
                columns: new[] { "Id", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "Bergrivier Municipality" },
                    { 2, true, "Cederberg Municipality" },
                    { 3, true, "Hessequa Municipality" },
                    { 4, true, "Swartland Municipality" },
                    { 5, true, "Witzenberg Municipality" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Municipalities_Name",
                table: "Municipalities",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Municipalities");
        }
    }
}
