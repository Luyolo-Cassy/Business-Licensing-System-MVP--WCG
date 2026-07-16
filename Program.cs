using BusinessLicensing_Practice.Components;
using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Components.Authorization;
using BusinessLicensing_Practice.Components.Account;
using Microsoft.AspNetCore.Identity.UI.Services;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Components.Account;
using BusinessLicensing_Practice.Data;


var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
        FullName = "Municipal Official"
    };

    var result = await userManager.CreateAsync(official, "Password123!");

    if (result.Succeeded)
    {
        await userManager.AddToRoleAsync(official, "MunicipalOfficial");
    }
}
    

    if (!db.Applications.Any())
    {
        db.Applications.AddRange(
            new Application
            {
                ApplicationNumber = "APP-2026-001",
                BusinessName = "Thembi's Coffee Shop",
                LicenceType = "Food Licence",
                Status = "In Progress",
                DateSubmitted = new DateTime(2026, 5, 1)
            },
            new Application
            {
                ApplicationNumber = "APP-2026-002",
                BusinessName = "Dube Mini Market",
                LicenceType = "Retail Licence",
                Status = "Approved",
                DateSubmitted = new DateTime(2026, 4, 18)
            },
            new Application
            {
                ApplicationNumber = "APP-2026-003",
                BusinessName = "Alec Events",
                LicenceType = "Entertainment Licence",
                Status = "Rejected",
                DateSubmitted = new DateTime(2026, 4, 10)
            }
        );

        db.SaveChanges();
    }
}

app.Run();
