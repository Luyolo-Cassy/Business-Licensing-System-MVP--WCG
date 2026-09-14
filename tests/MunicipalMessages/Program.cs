using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

var database = Path.Combine(Path.GetTempPath(), $"municipal-messages-{Guid.NewGuid():N}.db");
ServiceProvider CreateProvider()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database}"));
    services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>();
    services.AddScoped<MunicipalMessageService>();
    return services.BuildServiceProvider();
}
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
}
ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, id)], "test"));
async Task Denied(Func<Task> action)
{
    try { await action(); }
    catch (UnauthorizedAccessException) { return; }
    throw new Exception("Expected authorization rejection");
}

int applicationId, otherApplicationId;
string ownerId, otherOwnerId, officialId, wrongOfficialId;
var longMessage = "Please supply the following:\n" + new string('x', 20000) + "\n<script>alert('test')</script>";
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.GetService<IMigrator>().MigrateAsync("20260909105035_RenamePlaceOfBusinessAddress");
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "BusinessOwner", "MunicipalOfficial" })
        Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded, $"Create {role} role");
    async Task<string> User(string name, string role, string? municipality = null)
    {
        var user = new ApplicationUser { UserName = name, FullName = name, Municipality = municipality };
        Check((await users.CreateAsync(user, "Password123!")).Succeeded, $"Create {name}");
        await users.AddToRoleAsync(user, role);
        Check(await users.CheckPasswordAsync(user, "Password123!"), $"Authenticate {name}");
        return user.Id;
    }
    ownerId = await User("owner", "BusinessOwner");
    otherOwnerId = await User("other-owner", "BusinessOwner");
    officialId = await User("official", "MunicipalOfficial", "City of Cape Town");
    wrongOfficialId = await User("other-official", "MunicipalOfficial", "Other municipality");
    var application = new Application { UserId = ownerId, Municipality = "City of Cape Town", ApplicationNumber = "TEST-1", Status = "Submitted" };
    var other = new Application { UserId = otherOwnerId, Municipality = "Other municipality", ApplicationNumber = "TEST-2", Status = "Submitted" };
    db.Applications.AddRange(application, other);
    await db.SaveChangesAsync();
    applicationId = application.Id;
    otherApplicationId = other.Id;
    await db.Database.MigrateAsync();
    Check(!db.Database.HasPendingModelChanges(), "Current model matches migrations; no migration required");
    Check(await db.Applications.CountAsync() == 2, "Migration preserves existing applications");
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", longMessage);
    var saved = await db.MunicipalMessages.SingleAsync();
    Check(saved.Content == longMessage && saved.SenderId == officialId && saved.CreatedAtUtc != default && saved.ReadAtUtc == null, "Full text, sender, timestamp and unread notification saved");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", "   ");
    Check(await db.MunicipalMessages.CountAsync() == 1, "Status-only review does not create empty notification");
    await Denied(() => service.SaveReviewAsync(Principal(wrongOfficialId), applicationId, "Rejected", "Forbidden"));
    await Denied(() => service.SaveReviewAsync(Principal(ownerId), applicationId, "Rejected", "Forbidden"));
    Check(await db.MunicipalMessages.CountAsync() == 1, "Municipality and role restrictions enforced");
    var unrouted = new Application { UserId = ownerId, Municipality = null, Status = "Submitted" };
    db.Applications.Add(unrouted);
    await db.SaveChangesAsync();
    await Denied(() => service.SaveReviewAsync(Principal(officialId), unrouted.Id, "Rejected", "Forbidden"));
    Check(unrouted.Status == "Submitted", "Official cannot review an unrouted legacy application");
}
// Dispose the entire provider and reopen the file database to simulate process restart.
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Unread notification survives restart and identifies application");
    Check((await service.GetMessagesAsync(Principal(otherOwnerId), applicationId)).Count == 0, "Another applicant cannot view messages");
    Check((await service.GetUnreadCountsAsync(Principal(otherOwnerId))).Count == 0, "Another applicant cannot see notification counts");
    var displayed = await service.GetMessagesAsync(Principal(ownerId), applicationId);
    Check(displayed.Single().Content == longMessage, "Message text survives restart");
    await service.MarkReadAsync(Principal(otherOwnerId), applicationId, [displayed[0].Id]);
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Another applicant cannot mark messages read");
    await service.SaveReviewAsync(Principal(officialId), applicationId, "Under Review", "Follow-up");
    await service.MarkReadAsync(Principal(ownerId), applicationId, displayed.Select(m => m.Id).ToArray());
    Check((await service.GetUnreadCountsAsync(Principal(ownerId)))[applicationId] == 1, "Reading displayed messages leaves concurrent new message unread");
    var all = await service.GetMessagesAsync(Principal(ownerId), applicationId);
    Check(all.Count == 2 && all[0].Content == longMessage && all[1].Content == "Follow-up", "Messages appear in chronological order");
    await service.MarkReadAsync(Principal(ownerId), applicationId, all.Select(m => m.Id).ToArray());
    Check((await service.GetUnreadCountsAsync(Principal(ownerId))).Count == 0, "Viewing all messages clears unread count");
}
await using (var provider = CreateProvider())
{
    await using var scope = provider.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<MunicipalMessageService>();
    Check((await service.GetUnreadCountsAsync(Principal(ownerId))).Count == 0, "Read state survives restart");
    Check((await service.GetMessagesAsync(Principal(ownerId), applicationId)).Count == 2, "Read messages remain available after restart");
}
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
File.Delete(database);
Console.WriteLine("All municipal message integration checks passed.");
