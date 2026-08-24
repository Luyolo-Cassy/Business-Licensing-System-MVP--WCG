# User Guide

## Business owner journey

### Create an account

1. Open the home page and select **Register**.
2. Enter your full name, email address, and password.
3. Submit the form. New registrations receive the `BusinessOwner` role and are signed in immediately.

Email confirmation is disabled in the current MVP.

### Submit an application

1. Open **New Application** from the navigation menu or dashboard.
2. Select one of the available licence categories.
3. Enter the required business, registration, tax, and address details.
4. Complete any licence-specific information.
5. Upload the completed official application form.
6. Upload all four required supporting documents:
   - Certificate of Incorporation
   - Proof of Address
   - Tax Clearance Certificate
   - Owner ID Document
7. Review the application and accept the POPIA consent statement.
8. Select **Submit Application**.

The system creates an identifier in the form `APP-YYYY-NNN`, sets the initial status to `Submitted`, and returns you to the dashboard.

### Track an application

The dashboard displays totals and the status of each application belonging to the signed-in user. Select **Track** beside an application to see its progress and submitted documents.

The normal stages are:

1. `Submitted`
2. `Under Review`
3. `Department Assessment`
4. `Final Decision`
5. `Licence Issued` or `Rejected`

An application may also be marked `Withdrawn`. A rejected or withdrawn application can be reset and resubmitted, or used as the starting point for a new application.

## Municipal official journey

### Sign in

For local demonstrations, use the seeded official account documented in the project README. Successful sign-in redirects municipal officials to `/official-dashboard`.

### Find and review applications

The official dashboard shows system-wide totals and all applications, newest first. Officials can:

- Search by business name or application number.
- Filter by current status.
- Open an application for review.
- View the application form and supporting documents.
- Change and save the processing status.
- Approve or reject an application when it reaches `Final Decision`.

### View reports

Open **Reports** to see:

- Counts by status
- Monthly submission volumes
- Distribution by licence type

The charts use the current records in the SQLite database and are rendered with Chart.js.

## Roles and access

| Area | Anonymous | BusinessOwner | MunicipalOfficial |
|---|---:|---:|---:|
| Home, login, registration | Yes | Yes | Yes |
| Business dashboard | No | Yes | No |
| New application | No | Yes | No |
| Application tracking | Authenticated route; owner actions require BusinessOwner | Yes | Limited |
| Official dashboard | No | No | Yes |
| Application review | No | No | Yes |
| Reports | No | No | Yes |

## Troubleshooting

### A page redirects to login or access denied

Confirm that you are signed in with the appropriate role. Self-registered users are business owners; official access is seeded by the application.

### An upload fails

Keep each file below 10 MB and ensure the process can write to `wwwroot/uploads`. The directory is created on the first successful submission.

### Charts or icons do not appear

Chart.js and Bootstrap Icons are loaded from public CDNs, so they require an internet connection unless these dependencies are hosted locally.
