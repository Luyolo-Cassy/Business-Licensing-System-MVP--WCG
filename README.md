# Provincial Business Licensing System

A web-based MVP for submitting, tracking, reviewing, and reporting on provincial business licence applications. The application provides separate experiences for business owners and municipal officials.

## Features

### Business owners

- Register and sign in with an email address.
- Submit a guided, multi-step licence application.
- Upload a completed application form and supporting documents.
- View all applications on a personal dashboard.
- Track an application through its processing stages.
- Withdraw or reapply after a rejected or withdrawn application.

### Municipal officials

- View, search, and filter all submitted applications.
- Inspect applicant details and uploaded documents.
- Move applications through the review workflow.
- Approve or reject applications at the final-decision stage.
- View status, monthly-volume, and licence-type reports.

## Technology stack

- .NET 10 and ASP.NET Core
- Blazor interactive server components
- ASP.NET Core Identity with role-based authorization
- Entity Framework Core 10
- SQLite
- Bootstrap, Bootstrap Icons, and Chart.js

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A browser

### Run locally

```powershell
dotnet restore
dotnet run --project ProvincialBusinessLicensingSystem.csproj
```

Open the URL printed by `dotnet run`. The checked-in launch profile uses:

- `https://localhost:7290`
- `http://localhost:5209`

The application automatically applies Entity Framework migrations and creates the required roles on startup.

### Development accounts

Register through `/Account/Register` to create a `BusinessOwner` account.

A municipal official is seeded on first startup:

| Field | Value |
|---|---|
| Email | `official@westerncape.gov.za` |
| Password | `Password123!` |
| Role | `MunicipalOfficial` |

> These credentials are for local demonstration only. Remove the seeded password and use secure provisioning before any deployment.

## Application workflow

```text
Submitted -> Under Review -> Department Assessment -> Final Decision
                                                        |-> Licence Issued
                                                        `-> Rejected

Business owners may also withdraw an application. Rejected or withdrawn applications can be resubmitted.
```

New applications require:

- Business and address information
- A completed application-form upload
- Certificate of Incorporation
- Proof of Address
- Tax Clearance Certificate
- Owner ID Document
- POPIA consent

Each file is limited to 10 MB by the application code.

## Project structure

```text
Components/
  Account/       Identity UI and account-management components
  Layout/        Shared layout and navigation
  Pages/         Business-owner and municipal-official pages
Data/            Entity Framework database context
Migrations/      Database schema migrations
Models/          Application, document, and user entities
Properties/      Local launch profiles
wwwroot/         Styles, scripts, forms, and runtime uploads
Program.cs       Service registration, middleware, migrations, and role seeding
```

## Documentation

- [User guide](docs/USER_GUIDE.md)
- [Architecture and data model](docs/ARCHITECTURE.md)
- [Developer guide](docs/DEVELOPMENT.md)

## Current MVP limitations

- SQLite and local file storage are intended for a single-instance deployment.
- Uploaded documents are served from a public static-files path; production systems should use protected object storage and authorized download endpoints.
- The configured email sender does not send email, so password recovery and confirmation mail are not operational.
- Account confirmation is disabled.
- The `DEDATAdmin` role is created, but no admin dashboard is implemented.
- Automated tests are not currently included.
- Application numbers are derived from the annual row count and are not concurrency-safe.
- The downloadable-form mapping still uses legacy licence names, while the application screen exposes seven more specific licence categories.

## Licence

No licence file is currently included. Add one before distributing or reusing the project outside its intended academic context.
