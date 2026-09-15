using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalityRoutingName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoutingName",
                table: "Municipalities",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Municipalities",
                keyColumn: "Id",
                keyValue: 1,
                column: "RoutingName",
                value: "Bergrivier Municipality");

            migrationBuilder.UpdateData(
                table: "Municipalities",
                keyColumn: "Id",
                keyValue: 2,
                column: "RoutingName",
                value: "Cederberg Municipality");

            migrationBuilder.UpdateData(
                table: "Municipalities",
                keyColumn: "Id",
                keyValue: 3,
                column: "RoutingName",
                value: "Hessequa Municipality");

            migrationBuilder.UpdateData(
                table: "Municipalities",
                keyColumn: "Id",
                keyValue: 4,
                column: "RoutingName",
                value: "Swartland Municipality");

            migrationBuilder.UpdateData(
                table: "Municipalities",
                keyColumn: "Id",
                keyValue: 5,
                column: "RoutingName",
                value: "Witzenberg Municipality");

            migrationBuilder.CreateIndex(
                name: "IX_Municipalities_RoutingName",
                table: "Municipalities",
                column: "RoutingName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Municipalities_RoutingName",
                table: "Municipalities");

            migrationBuilder.DropColumn(
                name: "RoutingName",
                table: "Municipalities");
        }
    }
}
