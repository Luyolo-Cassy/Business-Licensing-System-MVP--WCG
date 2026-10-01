using BusinessLicensing_Practice.Components;
using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using BusinessLicensing_Practice.Components.Account;
using Microsoft.AspNetCore.Identity.UI.Services;
using BusinessLicensing_Practice.Services;
using BusinessLicensing_Practice.Services.Email;
using System.Security.Claims;
using PdfSharp.Fonts;
using BusinessLicensing_Practice.Fonts;
using BusinessLicensing_Practice.Services.Storage;
using Amazon.Runtime;
using Amazon.S3;

GlobalFontSettings.FontResolver = new LiberationSansFontResolver();

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPrivateFileStore, LocalPrivateFileStore>();
}
else if (builder.Environment.IsProduction())
{
    builder.Services.AddOptions<CloudflareR2Options>()
        .Bind(builder.Configuration.GetSection(CloudflareR2Options.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddSingleton<IAmazonS3>(services =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudflareR2Options>>().Value;
        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.Endpoint.TrimEnd('/'),
                AuthenticationRegion = "auto",
                ForcePathStyle = true
            });
    });
    builder.Services.AddSingleton<IPrivateFileStore, CloudflareR2PrivateFileStore>();
}
else
{
    throw new InvalidOperationException(
        $"No IPrivateFileStore implementation is configured for environment '{builder.Environment.EnvironmentName}'.");
}

var databaseProvider = builder.Configuration["Database:Provider"]?.Trim();
if (string.IsNullOrWhiteSpace(databaseProvider))
    throw new InvalidOperationException("Database:Provider must be configured as SQLite or PostgreSQL.");

var usesSqlite = string.Equals(databaseProvider, "SQLite", StringComparison.OrdinalIgnoreCase);
var usesPostgreSql = string.Equals(databaseProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
if (!usesSqlite && !usesPostgreSql)
    throw new InvalidOperationException($"Unsupported database provider '{databaseProvider}'. Configure Database:Provider as SQLite or PostgreSQL.");

var databaseConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(databaseConnection))
    throw new InvalidOperationException($"ConnectionStrings:DefaultConnection must be configured when using {databaseProvider}.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (usesSqlite)
        options.UseSqlite(databaseConnection);
    else
        options.UseNpgsql(databaseConnection, npgsql =>
            npgsql.MigrationsAssembly("BusinessLicensing.PostgreSqlMigrations"));
});

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
    options.SignIn.RequireConfirmedAccount = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Revoke changed/deactivated accounts on the next HTTP request as well as circuit actions.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// Add services to the container.
builder.Services.AddTransient<IEmailSender<ApplicationUser>, IdentityEmailSender>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddTransient<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IApplicationNotificationService, ApplicationNotificationService>();
builder.Services.AddScoped<ApplicationDraftService>();
builder.Services.AddScoped<ApplicationCorrectionService>();
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
builder.Services.AddScoped<InitialAdminBootstrapper>();
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

// Preserve historical local files and DB references; this migration is never run for a remote store.
if (app.Environment.IsDevelopment())
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

    var file = await fileService.OpenGeneratedPdfAsync(application.ApplicationFormFilePath, context.RequestAborted);
    if (file == null)
    {
        return Results.NotFound();
    }

    return Results.Stream(file.Content, "application/pdf", application.ApplicationFormFileName);
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

    // Local SQLite retains its established automatic migration behavior. PostgreSQL migrations
    // are applied as a controlled deployment step, not by every production application instance.
    if (usesSqlite) db.Database.Migrate();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

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
            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(error => error.Code));
                throw new InvalidOperationException($"Could not create Identity role '{role}': {errors}");
            }
        }
    }

    await scope.ServiceProvider.GetRequiredService<InitialAdminBootstrapper>().BootstrapAsync();

}

app.Run();
