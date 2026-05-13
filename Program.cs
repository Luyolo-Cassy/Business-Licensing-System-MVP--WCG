using BusinessLicensing_Practice.Components;
using Microsoft.EntityFrameworkCore;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=businesslicensing.db"));

// Add services to the container.
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    db.Database.EnsureCreated();

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
                BusinessName = "Thembi's Mini Market",
                LicenceType = "Retail Licence",
                Status = "Approved",
                DateSubmitted = new DateTime(2026, 4, 18)
            },
            new Application
            {
                ApplicationNumber = "APP-2026-003",
                BusinessName = "Thembi Events",
                LicenceType = "Entertainment Licence",
                Status = "Rejected",
                DateSubmitted = new DateTime(2026, 4, 10)
            }
        );

        db.SaveChanges();
    }
}

app.Run();
