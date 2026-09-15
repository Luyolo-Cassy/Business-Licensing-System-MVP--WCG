using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var database = Path.Combine(Path.GetTempPath(), $"municipality-management-{Guid.NewGuid():N}.db");
var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database}"));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<MunicipalityManagementService>();
services.AddScoped<MunicipalMessageService>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await db.Database.MigrateAsync();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var management = scope.ServiceProvider.GetRequiredService<MunicipalityManagementService>();
void Check(bool ok, string description)
{
    if (!ok) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
async Task<ClaimsPrincipal> User(string role)
{
    await roles.CreateAsync(new IdentityRole(role));
    var user = new ApplicationUser { UserName = role, Municipality = "Bergrivier Municipality" };
    Check((await users.CreateAsync(user, "TestOnly!2026#")).Succeeded, "Create " + role);
    await users.AddToRoleAsync(user, role);
    return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
}
var admin = await User("DEDATAdmin");
var owner = await User("BusinessOwner");
var official = await User("MunicipalOfficial");
foreach (var denied in new[] { owner, official, new ClaimsPrincipal() })
{
    await Reject<UnauthorizedAccessException>(() => management.ListAsync(denied));
    await Reject<UnauthorizedAccessException>(() => management.SaveAsync(denied, null, "Forbidden"));
    await Reject<UnauthorizedAccessException>(() => management.SaveAsync(denied, 1, "Forbidden", "Bergrivier Municipality"));
    await Reject<UnauthorizedAccessException>(() => management.SetActiveAsync(denied, 1, false));
}
Check(true, "Non-admins denied every management operation");
Check((await management.ListAsync(admin)).Count == 5, "All five municipalities listed");
await management.SaveAsync(admin, null, "  Test Municipality  ");
var added = (await management.ListAsync(admin)).Single(m => m.Name == "Test Municipality");
Check(added.IsActive && added.RoutingName == null, "Add trims name, defaults active, does not enable routing");
await Reject<ValidationException>(() => management.SaveAsync(admin, null, " test MUNICIPALITY "));
await Reject<ValidationException>(() => management.SaveAsync(admin, null, "   "));
await Reject<ValidationException>(() => management.SaveAsync(admin, null, new string('a', 201)));
await Reject<ValidationException>(() => management.SaveAsync(admin, null, "Bergrivier Local Municipality"));
Check(true, "Duplicate, blank, long and reserved geographic alias names rejected");
var application = new Application { Municipality = "Bergrivier Municipality", UserId = owner.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = "Submitted", ApplicationFormFilePath = "existing.pdf" };
db.Applications.Add(application);
await db.SaveChangesAsync();
var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
await messages.SaveReviewAsync(official, application.Id, "Under Review", "Historical message");
await management.SetActiveAsync(admin, 1, false);
await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Bergrivier Municipality"));
await messages.SaveReviewAsync(official, application.Id, "Under Review", "Inactive municipality message");
Check(await db.Applications.CountAsync() == 1 && await db.MunicipalMessages.CountAsync() == 2 && await db.Users.CountAsync() == 3,
    "Deactivation preserves applications/users/messages and historical review access");
await management.SetActiveAsync(admin, 1, true);
Check((await MunicipalityManagementService.RequireActiveRoutingAsync(db, "Bergrivier Municipality")).IsActive, "Reactivation passes new-submission gate");
await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Missing Municipality"));
await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Test Municipality"));
Check(LicenceApplicationCatalog.MapMunicipality("Test Municipality") == null, "New database municipality remains geographically unsupported");
await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_rename BEFORE UPDATE OF Municipality ON AspNetUsers BEGIN SELECT RAISE(ABORT, 'test rollback'); END;");
await Reject<Microsoft.Data.Sqlite.SqliteException>(() => management.SaveAsync(admin, 1, "Renamed Municipality", "Bergrivier Municipality"));
Check(await db.Applications.AsNoTracking().AnyAsync(a => a.Municipality == "Bergrivier Municipality") &&
    (await management.ListAsync(admin)).Single(m => m.Id == 1).Name == "Bergrivier Municipality", "Failed rename rolls back application and municipality changes");
await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_rename;");
await management.SaveAsync(admin, 1, "  Renamed Municipality  ", "Bergrivier Municipality");
db.ChangeTracker.Clear();
Check(await db.Applications.AllAsync(a => a.Municipality == "Renamed Municipality") && await db.Users.AllAsync(u => u.Municipality == "Renamed Municipality"), "Rename atomically updates all matching application/user strings");
Check((await MunicipalityManagementService.RequireActiveRoutingAsync(db, "Bergrivier Municipality")).Name == "Renamed Municipality", "Original routing result resolves renamed municipality");
await Reject<ValidationException>(() => management.SaveAsync(admin, 1, "Stale Edit", "Bergrivier Municipality"));
await Reject<ValidationException>(() => management.SaveAsync(admin, 1, "Test Municipality", "Renamed Municipality"));
await management.SetActiveAsync(admin, 1, false);
await messages.SaveReviewAsync(official, application.Id, "Under Review", "After rename");
var assigned = await users.GetUserAsync(official);
Check(await db.Applications.CountAsync(a => a.Municipality == assigned!.Municipality) == 1, "Existing Official dashboard/report/review municipality predicate matches inactive renamed history");
Check((await messages.GetMessagesAsync(owner, application.Id)).Count == 3, "Applicant messaging survives rename/deactivation");
Check((await db.Applications.AsNoTracking().SingleAsync()).ApplicationFormFilePath == "existing.pdf", "Generated PDF reference preserved");
Check(!db.Database.HasPendingModelChanges(), "No pending EF model changes");
Console.WriteLine("Test database: " + database);
