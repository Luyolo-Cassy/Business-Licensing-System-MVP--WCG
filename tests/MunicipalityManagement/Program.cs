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
    await Reject<UnauthorizedAccessException>(() => management.EligibleForAddAsync(denied));
    await Reject<UnauthorizedAccessException>(() => management.AddFromCatalogAsync(denied, "Matzikama Municipality"));
    await Reject<UnauthorizedAccessException>(() => management.SaveAsync(denied, 1, "Forbidden", "Bergrivier Municipality"));
    await Reject<UnauthorizedAccessException>(() => management.SetActiveAsync(denied, 1, false));
}
Check(true, "Non-admins denied every municipality management operation");

var initial = await management.ListAsync(admin);
Check(initial.Count == 5 && initial.All(m => m.IsActive && m.RoutingName != null), "Existing five participating municipalities preserved");
var eligible = await management.EligibleForAddAsync(admin);
Check(eligible.Count == 20, "Twenty additional predefined municipalities initially eligible");
Check(initial.All(existing => eligible.All(item => item.RoutingName != existing.RoutingName)), "Existing participants excluded from Add choices");

await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Matzikama Municipality"));
Check(true, "Recognised catalogue municipality without database row is blocked");
await Reject<ValidationException>(() => management.AddFromCatalogAsync(admin, "forged-routing-identity"));
await Reject<ValidationException>(() => management.SaveAsync(admin, null, "Arbitrary Municipality"));
Check(true, "Forged catalogue selection and arbitrary creation rejected");

await management.AddFromCatalogAsync(admin, "Matzikama Municipality");
var matzikama = (await management.ListAsync(admin)).Single(m => m.RoutingName == "Matzikama Municipality");
Check(matzikama.Name == "Matzikama Municipality" && matzikama.IsActive, "Catalogue controls new municipality name, routing identity and active state");
await Reject<ValidationException>(() => management.AddFromCatalogAsync(admin, "Matzikama Municipality"));
Check((await management.EligibleForAddAsync(admin)).All(item => item.RoutingName != "Matzikama Municipality"), "Added municipality excluded from choices");
Check((await MunicipalityManagementService.RequireActiveRoutingAsync(db, "Matzikama Municipality")).Id == matzikama.Id,
    "Adding participant enables an already-catalogued routing identity");

await management.SaveAsync(admin, matzikama.Id, "Matzikama Licensing Municipality", matzikama.Name);
Check((await management.EligibleForAddAsync(admin)).All(item => item.RoutingName != "Matzikama Municipality"), "Renamed participant remains excluded by RoutingName");
Check((await MunicipalityManagementService.RequireActiveRoutingAsync(db, "Matzikama Municipality")).Name == "Matzikama Licensing Municipality",
    "Renamed participant resolves through stable RoutingName to current Name");
await management.SetActiveAsync(admin, matzikama.Id, false);
await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Matzikama Municipality"));
Check((await management.EligibleForAddAsync(admin)).All(item => item.RoutingName != "Matzikama Municipality"), "Deactivated participant remains excluded from Add choices");
await management.SetActiveAsync(admin, matzikama.Id, true);

var legacy = new Municipality { Name = "Legacy Municipality", RoutingName = null, IsActive = true };
db.Municipalities.Add(legacy);
await db.SaveChangesAsync();
await management.SetActiveAsync(admin, legacy.Id, false);
Check(await db.Municipalities.AnyAsync(m => m.Id == legacy.Id && m.RoutingName == null), "Legacy non-routable municipality preserved");
db.Municipalities.Add(new Municipality { Name = "saldanha bay municipality", RoutingName = null, IsActive = false });
await db.SaveChangesAsync();
await Reject<ValidationException>(() => management.AddFromCatalogAsync(admin, "Saldanha Bay Municipality"));
Check(true, "Legacy name collision fails safely without automatic linking");

var application = new Application { Municipality = "Bergrivier Municipality", UserId = owner.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = "Submitted", ApplicationFormFilePath = "existing.pdf" };
db.Applications.Add(application);
await db.SaveChangesAsync();
var messages = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
await messages.SaveReviewAsync(official, application.Id, "Under Review", "Historical message");
await management.SetActiveAsync(admin, 1, false);
await Reject<ValidationException>(() => MunicipalityManagementService.RequireActiveRoutingAsync(db, "Bergrivier Municipality"));
await messages.SaveReviewAsync(official, application.Id, "Under Review", "Inactive municipality message");
Check(await db.Applications.CountAsync(a => a.Id == application.Id) == 1, "Deactivation preserves historical applications");
await management.SetActiveAsync(admin, 1, true);
await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_rename BEFORE UPDATE OF Municipality ON AspNetUsers BEGIN SELECT RAISE(ABORT, 'test rollback'); END;");
await Reject<Microsoft.Data.Sqlite.SqliteException>(() => management.SaveAsync(admin, 1, "Renamed Municipality", "Bergrivier Municipality"));
Check(await db.Applications.AsNoTracking().AnyAsync(a => a.Municipality == "Bergrivier Municipality") &&
    (await management.ListAsync(admin)).Single(m => m.Id == 1).Name == "Bergrivier Municipality", "Failed rename rolls back application and municipality changes");
await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_rename;");
await management.SaveAsync(admin, 1, "Renamed Municipality", "Bergrivier Municipality");
db.ChangeTracker.Clear();
Check(await db.Applications.AnyAsync(a => a.Municipality == "Renamed Municipality") && await db.Users.AllAsync(u => u.Municipality == "Renamed Municipality"),
    "Rename atomically updates application and official assignments");
Check((await MunicipalityManagementService.RequireActiveRoutingAsync(db, "Bergrivier Municipality")).Name == "Renamed Municipality",
    "Original participant routing identity survives rename");
await Reject<ValidationException>(() => management.SaveAsync(admin, 1, "Stale Edit", "Bergrivier Municipality"));
Check((await messages.GetMessagesAsync(owner, application.Id)).Count == 2, "Applicant messaging survives rename and activation changes");
Check((await db.Applications.AsNoTracking().SingleAsync(a => a.Id == application.Id)).ApplicationFormFilePath == "existing.pdf", "Generated PDF reference preserved");
Check(!db.Database.HasPendingModelChanges(), "No pending EF model changes");
Console.WriteLine("Test database: " + database);
