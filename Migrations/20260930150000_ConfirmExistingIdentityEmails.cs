using BusinessLicensing_Practice.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930150000_ConfirmExistingIdentityEmails")]
public partial class ConfirmExistingIdentityEmails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Grandfather only Identity users present when this migration is applied.
        // Future users retain Identity's EmailConfirmed=false default.
        migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"EmailConfirmed\" = 1 WHERE \"EmailConfirmed\" = 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Confirmation cannot be safely reversed because later users may have confirmed normally.
    }
}
