# Developer Guide

## Set up the project

```powershell
git clone <repository-url>
cd Business-Licensing-System-MVP--WCG
dotnet restore
dotnet build ProvincialBusinessLicensingSystem.sln
dotnet run --project ProvincialBusinessLicensingSystem.csproj
```

Use `dotnet watch` during UI development:

```powershell
dotnet watch --project ProvincialBusinessLicensingSystem.csproj
```

## Database management

The application uses `businesslicensing.db` in the working directory. Startup calls `Database.Migrate()`, so committed migrations are applied automatically.

Install the EF Core CLI tool if it is not already available:

```powershell
dotnet tool install --global dotnet-ef
```

Create and apply a schema change:

```powershell
dotnet ef migrations add <MigrationName> --project ProvincialBusinessLicensingSystem.csproj
dotnet ef database update --project ProvincialBusinessLicensingSystem.csproj
```

Do not edit generated migration designer files by hand. Review the generated migration before committing it.

## Configuration

Logging is configured in `appsettings.json` and `appsettings.Development.json`. The SQLite connection is currently hard-coded in `Program.cs`; a production-ready change should move it into configuration, for example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=businesslicensing.db"
  }
}
```

Then read it with `builder.Configuration.GetConnectionString("DefaultConnection")`.

Never commit production passwords, API keys, or personally identifiable uploaded documents.

## Adding a page

1. Add a `.razor` component under `Components/Pages`.
2. Add an `@page` directive.
3. Apply an authorization attribute if required.
4. Add navigation only inside the appropriate `AuthorizeView` in `Components/Layout/NavMenu.razor`.
5. Prefer asynchronous EF Core calls and scope queries to the signed-in user where applicable.

## Updating the workflow

Status strings are currently repeated across dashboards, tracking, reviews, and reports. When adding or renaming a status, search all components and update:

- Status dropdowns and progression order
- Badge mappings
- Dashboard counts
- Tracking progress calculation
- Report aggregations

A future refactor should centralize statuses as constants or an enum plus a workflow service.

## Upload handling

The new-application page reads each upload into memory with a 10 MB maximum and writes it to `wwwroot/uploads`. Changes to upload requirements should keep UI validation, server-side validation, data fields, and review displays in sync.

For production, replace public local-file storage with a service that provides malware scanning, content-type validation, private storage, retention rules, and authorized downloads.

## Verification checklist

Before submitting a change:

```powershell
dotnet restore
dotnet build ProvincialBusinessLicensingSystem.sln
dotnet test ProvincialBusinessLicensingSystem.sln
```

There is currently no test project, so `dotnet test` primarily verifies that the solution loads successfully. Manually exercise both roles:

- Register and sign in as a business owner.
- Submit an application with all required documents.
- Confirm it appears only on that owner's dashboard.
- Sign in as the municipal official.
- Search, review, advance, approve, and reject applications.
- Verify tracking reflects changes.
- Open reports and inspect all three charts.

## Recommended next improvements

1. Add unit and integration tests for authorization, submission, and status transitions.
2. Add owner checks to ID-based tracking queries.
3. Secure uploaded documents and validate their content.
4. Move secrets and connection details into environment configuration.
5. Replace the development account seed with an administrative provisioning flow.
6. Centralize application statuses and licence categories.
7. Resolve the mismatch between the seven current licence categories and the three downloadable-form mappings.
