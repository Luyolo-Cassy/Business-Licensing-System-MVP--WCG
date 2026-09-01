# Architecture

## Overview

The system is a single ASP.NET Core application using Blazor interactive server rendering. UI events execute on the server, Entity Framework Core persists records to SQLite, and ASP.NET Core Identity manages users, cookies, and roles.

```text
Browser
  |  HTTPS + Blazor SignalR connection
  v
Razor components
  |-- Identity / role authorization
  |-- Application workflows
  |-- JS interop -> Chart.js
  v
ApplicationDbContext
  |-- Identity tables
  |-- Applications
  `-- ApplicationDocuments
  v
SQLite database (businesslicensing.db)

Uploaded files -> wwwroot/uploads -> static-file URL
```

## Startup and request pipeline

`Program.cs` performs the following work:

1. Registers `ApplicationDbContext` with the SQLite database.
2. Configures Identity cookies, roles, token providers, and authentication state.
3. Registers interactive server-side Razor components.
4. Enables HTTPS redirection, authentication, authorization, antiforgery, and static assets.
5. Maps the Razor component and Identity endpoints.
6. Applies outstanding migrations.
7. Creates `BusinessOwner`, `MunicipalOfficial`, and `DEDATAdmin` roles.
8. Seeds the development municipal-official account.

## Core entities

### ApplicationUser

Extends `IdentityUser` with:

- `FullName`
- Optional `Municipality`
- A collection of submitted applications

### Application

Stores the application number, business identity, address, licence-specific fields, consent, status, submission date, owning user, application-form metadata, and supporting-document collection.

### ApplicationDocument

Stores a document type, original file name, public file path, and parent application ID.

Relationships:

```text
ApplicationUser 1 ---- * Application 1 ---- * ApplicationDocument
```

## Main routes

| Route | Component | Access |
|---|---|---|
| `/` | `Home.razor` | Public |
| `/dashboard` | `Dashboard.razor` | BusinessOwner |
| `/new-application` | `NewApplication.razor` | BusinessOwner |
| `/tracking/{id}` | `Tracking.razor` | Authenticated by the global router |
| `/official-dashboard` | `OfficialDashboard.razor` | MunicipalOfficial |
| `/review-application/{id}` | `ReviewApplications.razor` | MunicipalOfficial |
| `/generate-report` | `OfficialReports.razor` | MunicipalOfficial |
| `/Account/*` | Identity components | Varies by operation |

Route matching is case-insensitive in normal ASP.NET Core hosting, although route declarations use mixed casing in the source.

## Persistence and files

The connection string is currently embedded in `Program.cs` as `Data Source=businesslicensing.db`. Database migrations live in `Migrations/` and are automatically applied every time the app starts.

Application forms and supporting documents are buffered in memory, written to `wwwroot/uploads` using GUID-prefixed file names, and referenced by a public `/uploads/...` URL. Metadata is stored only after the application record has received its database ID.

## Authentication and authorization

Identity uses application cookies. Registration assigns every new user the `BusinessOwner` role. Page-level `[Authorize(Roles = ...)]` attributes protect owner and official pages, while the router redirects unauthorized visitors to login.

Important production considerations:

- Move seeded credentials to secure, environment-specific provisioning.
- Configure a real email sender and confirmation policy.
- Enforce ownership checks whenever an application is loaded by route ID.
- Validate file type as well as size, scan uploads, and serve them through authorization-aware endpoints.
- Move connection settings out of source code.
- Use database-generated sequences or unique constraints for application numbers.

## Front-end dependencies

Bootstrap is supplied through the ASP.NET project assets. Bootstrap Icons and Chart.js are loaded from CDNs in `Components/App.razor`. Report components invoke functions in `wwwroot/js/reportCharts.js` through Blazor JavaScript interop.
