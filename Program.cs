using BusinessLicensing_Practice.Components;
using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using BusinessLicensing_Practice.Components.Account;
using Microsoft.AspNetCore.Identity.UI.Services;
using BusinessLicensing_Practice.Services;
using System.Security.Claims;
using PdfSharp.Fonts;

var builder = WebApplication.CreateBuilder(args);

GlobalFontSettings.UseWindowsFontsUnderWindows = true;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=businesslicensing.db"));

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<IdentityRedirectManager>();

builder.Services.AddScoped<
    AuthenticationStateProvider,
    IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Revoke changed/deactivated accounts on the next HTTP request as well as circuit actions.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// Add services to the container.
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<ApplicationFileService>();
builder.Services.AddSingleton<ProtectedUploadService>();
builder.Services.AddSingleton<ApplicationPdfService>();
builder.Services.AddScoped<MunicipalMessageService>();
builder.Services.AddScoped<MunicipalityManagementService>();
builder.Services.AddScoped<OfficialManagementService>();
builder.Services.AddScoped<AdminApplicationService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddSingleton<ReportExportService>();
builder.Services.AddScoped<ApplicantApplicationService>();
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddScoped<AiDocumentValidationPolicy>();
builder.Services.AddHttpClient<IAiDocumentValidationService, GeminiDocumentValidationService>(client =>
    client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<ArcGisGeocodingService>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<WcgMunicipalBoundaryService>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<MunicipalRoutingService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Preserve historical files and DB references; only the physical storage location changes.
app.Services.GetRequiredService<ProtectedUploadService>().MoveLegacyUploads();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseAuthentication();
// Intercept legacy URLs before ANY static-asset/fallback middleware (including fingerprinted URLs).
app.Use(async (context, next) =>
{
    var segments = (context.Request.Path.Value ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length > 0 && string.Equals(segments[0], "uploads", StringComparison.OrdinalIgnoreCase))
    {
        var result = await context.RequestServices.GetRequiredService<ProtectedUploadService>().DownloadAsync(context,
            context.RequestServices.GetRequiredService<ApplicationDbContext>(), context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>());
        await result.ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGet("/applications/{id:int}/official-pdf", async (
    int id,
    HttpContext context,
    ClaimsPrincipal principal,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ApplicationFileService fileService) =>
{
    context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    var user = await userManager.GetUserAsync(principal);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var application = await db.Applications
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == id);

    if (application == null)
    {
        return Results.NotFound();
    }

    if (!await ApplicationReadAccess.CanReadAsync(db, userManager, principal, application))
    {
        return Results.Forbid();
    }

    var fullPath = fileService.GetGeneratedPdfPath(application.ApplicationFormFilePath);
    if (fullPath == null)
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, "application/pdf", application.ApplicationFormFileName);
}).RequireAuthorization();

app.MapGet("/reports/export", async (HttpContext context, ClaimsPrincipal principal, ReportingService reports, ReportExportService exports) =>
{
    context.Response.Headers.CacheControl = "private, no-store";
    var query = context.Request.Query;
    if (!Enum.TryParse<ReportAudience>(query["audience"], out var audience) || string.IsNullOrWhiteSpace(query["report"])) return Results.BadRequest();
    DateTime? ParseDate(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : null;
    var filter = new ReportFilter { From = ParseDate(query["from"]!), To = ParseDate(query["to"]!), Municipality = query["municipality"], LicenceType = query["licenceType"], ApplicationType = query["applicationType"], Status = query["status"], Outcome = query["outcome"], Grouping = string.IsNullOrWhiteSpace(query["grouping"]) ? "Monthly" : query["grouping"]! };
    try
    {
        var report = await reports.GenerateAsync(principal, audience, query["report"]!, filter); var now = DateTime.UtcNow;
        if (string.Equals(query["format"], "xlsx", StringComparison.OrdinalIgnoreCase)) return Results.File(exports.Excel(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ReportExportService.FileName(report, "xlsx", now));
        if (string.Equals(query["format"], "pdf", StringComparison.OrdinalIgnoreCase)) return Results.File(exports.Pdf(report, filter, now), "application/pdf", ReportExportService.FileName(report, "pdf", now));
        return Results.BadRequest();
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (ArgumentException error) { return Results.BadRequest(error.Message); }
}).RequireAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    db.Database.Migrate();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles =
    {
        "BusinessOwner",
        "MunicipalOfficial",
        "DEDATAdmin"
    };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    await DevelopmentAdminSeeder.SeedAsync(app.Environment, userManager);

}

app.Run();
