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

// Add services to the container.
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<ApplicationFileService>();
builder.Services.AddSingleton<ApplicationPdfService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGet("/applications/{id:int}/official-pdf", async (
    int id,
    ClaimsPrincipal principal,
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ApplicationFileService fileService) =>
{
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

    var isOwner = application.UserId == user.Id;
    var isAdmin = await userManager.IsInRoleAsync(user, "DEDATAdmin");
    var isAssignedOfficial = await userManager.IsInRoleAsync(user, "MunicipalOfficial")
        && !string.IsNullOrWhiteSpace(user.Municipality)
        && user.Municipality == application.Municipality;

    if (!isOwner && !isAdmin && !isAssignedOfficial)
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

    // Create a default Municipal Official
    var officialEmail = "official@westerncape.gov.za";

    var official = await userManager.FindByEmailAsync(officialEmail);

    if (official == null)
    {
        official = new ApplicationUser
        {
            UserName = officialEmail,
            Email = officialEmail,
            FullName = "Municipal Official",
            Municipality = "City of Cape Town"
        };

        var result = await userManager.CreateAsync(official, "Password123!");

        if (!result.Succeeded)
        {
            throw new Exception("Failed to create Municipal Official user.");
        }
    }

    if (string.IsNullOrWhiteSpace(official.Municipality))
    {
        official.Municipality = "City of Cape Town";
        await userManager.UpdateAsync(official);
    }

    // Ensure the user has the MunicipalOfficial role
    if (!await userManager.IsInRoleAsync(official, "MunicipalOfficial"))
    {
        await userManager.AddToRoleAsync(official, "MunicipalOfficial");
    }

    var municipalityOfficials = new[]
    {
        new { Email = "bergrivier.official@westerncape.gov.za", Name = "Bergrivier Municipal Official", Municipality = "Bergrivier Municipality" },
        new { Email = "cederberg.official@westerncape.gov.za", Name = "Cederberg Municipal Official", Municipality = "Cederberg Municipality" },
        new { Email = "hessequa.official@westerncape.gov.za", Name = "Hessequa Municipal Official", Municipality = "Hessequa Municipality" },
        new { Email = "swartland.official@westerncape.gov.za", Name = "Swartland Municipal Official", Municipality = "Swartland Municipality" },
        new { Email = "witzenberg.official@westerncape.gov.za", Name = "Witzenberg Municipal Official", Municipality = "Witzenberg Municipality" }
    };

    foreach (var municipalityOfficial in municipalityOfficials)
    {
        var municipalityUser = await userManager.FindByEmailAsync(municipalityOfficial.Email);

        if (municipalityUser == null)
        {
            municipalityUser = new ApplicationUser
            {
                UserName = municipalityOfficial.Email,
                Email = municipalityOfficial.Email,
                FullName = municipalityOfficial.Name,
                Municipality = municipalityOfficial.Municipality
            };

            var result = await userManager.CreateAsync(municipalityUser, "Password123!");
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                throw new Exception($"Failed to create {municipalityOfficial.Municipality} official: {errors}");
            }
        }
        else if (municipalityUser.Municipality != municipalityOfficial.Municipality ||
                 municipalityUser.FullName != municipalityOfficial.Name)
        {
            municipalityUser.Municipality = municipalityOfficial.Municipality;
            municipalityUser.FullName = municipalityOfficial.Name;
            await userManager.UpdateAsync(municipalityUser);
        }

        if (!await userManager.IsInRoleAsync(municipalityUser, "MunicipalOfficial"))
        {
            await userManager.AddToRoleAsync(municipalityUser, "MunicipalOfficial");
        }
    }
}

app.Run();
