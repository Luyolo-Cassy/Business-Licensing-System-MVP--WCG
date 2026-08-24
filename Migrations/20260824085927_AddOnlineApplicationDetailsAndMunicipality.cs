using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineApplicationDetailsAndMunicipality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Municipality",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationType = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicantName = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicantAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicantTelephone = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicantEmail = table.Column<string>(type: "TEXT", nullable: true),
                    PostalAddress = table.Column<string>(type: "TEXT", nullable: true),
                    ContactPerson = table.Column<string>(type: "TEXT", nullable: true),
                    BusinessTelephone = table.Column<string>(type: "TEXT", nullable: true),
                    BusinessEmail = table.Column<string>(type: "TEXT", nullable: true),
                    ErfNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Zoning = table.Column<string>(type: "TEXT", nullable: true),
                    TradingHours = table.Column<string>(type: "TEXT", nullable: true),
                    LicenceSpecificDetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                    DeclarationAccepted = table.Column<bool>(type: "INTEGER", nullable: true),
                    DeclarationAcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDetails_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDetails_ApplicationId",
                table: "ApplicationDetails",
                column: "ApplicationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "Municipality",
                table: "Applications");
        }
    }
}
