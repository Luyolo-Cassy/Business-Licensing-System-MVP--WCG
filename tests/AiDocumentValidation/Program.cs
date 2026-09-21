using System.Security.Claims;
using BusinessLicensing_Practice.Components.Pages;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"Failed: {name}");
    checks++;
    Console.WriteLine("PASS: " + name);
}

var validator = new FakeValidator();
var policy = new AiDocumentValidationPolicy(validator);
var bytes = new byte[] { 1, 2, 3 };

var disabled = await policy.EvaluateAsync(false, "Owner ID Document", "owner.pdf", bytes);
Check(disabled.Status == AiDocumentValidationStatus.Disabled && validator.Calls == 0,
    "AI OFF preserves Owner ID upload behavior and makes zero classifier calls");

var otherDocument = await policy.EvaluateAsync(true, "Proof of Address", "address.pdf", bytes);
Check(otherDocument.Status == AiDocumentValidationStatus.Disabled && validator.Calls == 0,
    "non-Owner-ID documents remain unaffected and make zero classifier calls");

validator.Next = AiDocumentValidationStatus.Match;
var match = await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes);
Check(match.Status == AiDocumentValidationStatus.Match && !AiDocumentValidationPolicy.BlocksProgress(match.Status),
    "AI ON Match allows progress");

validator.Next = AiDocumentValidationStatus.NoMatch;
var noMatch = await policy.EvaluateAsync(true, "Owner ID Document", "wrong.pdf", bytes);
Check(noMatch.Status == AiDocumentValidationStatus.NoMatch && AiDocumentValidationPolicy.BlocksProgress(noMatch.Status),
    "AI ON NoMatch blocks progress");

validator.Next = AiDocumentValidationStatus.Match;
var replacement = await policy.EvaluateAsync(true, "Owner ID Document", "replacement.pdf", bytes);
Check(replacement.Status == AiDocumentValidationStatus.Match && validator.FileNames.SequenceEqual(new[] { "owner.pdf", "wrong.pdf", "replacement.pdf" }),
    "replacement Owner ID is classified again and replaces the blocking result");

validator.Next = AiDocumentValidationStatus.Unavailable;
var unavailable = await policy.EvaluateAsync(true, "Owner ID Document", "owner.pdf", bytes);
Check(unavailable.Status == AiDocumentValidationStatus.Unavailable && !AiDocumentValidationPolicy.BlocksProgress(unavailable.Status),
    "AI unavailable does not block progress");

var noKeyHandler = new CountingHandler();
using (var http = new HttpClient(noKeyHandler))
{
    var gemini = new GeminiDocumentValidationService(http, new ConfigurationBuilder().Build());
    var missingConfiguration = await gemini.ValidateAsync(AiDocumentKind.OwnerIdentity, "owner.pdf", bytes);
    Check(missingConfiguration.Status == AiDocumentValidationStatus.Unavailable && noKeyHandler.Calls == 0,
        "missing Gemini configuration is unavailable without an HTTP call");
}

var database = Path.Combine(Path.GetTempPath(), $"ai-document-validation-{Guid.NewGuid():N}.db");
try
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database}"));
    services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>();
    services.AddScoped<AiSettingsService>();
    await using var provider = services.BuildServiceProvider();

    await using (var scope = provider.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var initialSettings = await db.AiSettings.SingleAsync();
        Check(!initialSettings.DocumentValidationEnabled, "AI document validation defaults to OFF");
        Check(!db.Database.HasPendingModelChanges(), "AI settings migration matches the EF model");

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "DEDATAdmin", "BusinessOwner", "MunicipalOfficial" })
            Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded, "create " + role + " role");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (name, role) in new[] { ("admin", "DEDATAdmin"), ("owner", "BusinessOwner"), ("official", "MunicipalOfficial") })
        {
            var user = new ApplicationUser { UserName = name + "@example.test", Email = name + "@example.test", FullName = name };
            Check((await users.CreateAsync(user)).Succeeded && (await users.AddToRoleAsync(user, role)).Succeeded,
                "create " + role + " user");
        }
    }

    ClaimsPrincipal Principal(ApplicationUser user) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id),
        new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!)
    }, "test"));

    await using (var scope = provider.CreateAsyncScope())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var settings = scope.ServiceProvider.GetRequiredService<AiSettingsService>();
        var admin = await users.FindByNameAsync("admin@example.test") ?? throw new Exception("Missing admin");
        await settings.SetDocumentValidationEnabledAsync(Principal(admin), true);
        Check(await settings.GetDocumentValidationEnabledAsync(Principal(admin)), "DEDATAdmin can manage AI setting");

        foreach (var name in new[] { "owner", "official" })
        {
            var user = await users.FindByNameAsync(name + "@example.test") ?? throw new Exception("Missing user");
            var denied = false;
            try { await settings.SetDocumentValidationEnabledAsync(Principal(user), false); }
            catch (UnauthorizedAccessException) { denied = true; }
            Check(denied, name + " cannot manage AI setting");
        }
    }

    await using (var scope = provider.CreateAsyncScope())
    {
        var settings = scope.ServiceProvider.GetRequiredService<AiSettingsService>();
        Check(await settings.IsDocumentValidationEnabledAsync(), "AI setting persists across context reload");
    }

    var authorization = typeof(AdminAiSettings).GetCustomAttributes(typeof(AuthorizeAttribute), true)
        .Cast<AuthorizeAttribute>().Single();
    Check(authorization.Roles == "DEDATAdmin", "AI Settings page requires DEDATAdmin role");
}
finally
{
    try { File.Delete(database); } catch (IOException) { }
}

Console.WriteLine($"AI document validation: {checks} checks passed. No Gemini requests were made.");

sealed class FakeValidator : IAiDocumentValidationService
{
    public int Calls { get; private set; }
    public List<string> FileNames { get; } = [];
    public AiDocumentValidationStatus Next { get; set; }

    public Task<AiDocumentValidationResult> ValidateAsync(AiDocumentKind documentKind, string fileName, byte[] fileBytes,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        FileNames.Add(fileName);
        return Task.FromResult(new AiDocumentValidationResult(Next));
    }
}

sealed class CountingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
