# Municipal messages and in-app notifications

The existing `ReviewComments` textarea and Save Review action are reused. Previously the textarea was only a component field: `SaveReview` saved the application but never persisted the comment. There was no stored note history to migrate or recover.

`MunicipalMessages` is the only new table. It stores the full message in SQLite `TEXT`, its application foreign key, sender user ID and name snapshot, UTC creation time, and nullable UTC read time. Each message doubles as its notification; a null read time means unread. Status changes and new messages commit together. Blank comments still allow status-only reviews without generating notifications.

The service rechecks the official's role and the existing municipality rule (including legacy applications with a null municipality). Applicant reads, unread counts, and read updates are scoped to the authenticated application's owner. Razor renders message text as escaped text and preserves line breaks. Tracking displays oldest first, breaking timestamp ties by message ID.

The applicant dashboard shows an unread total and application-specific badges linking to tracking. Counts reload on dashboard navigation or Refresh notifications. Tracking marks only the displayed message IDs read after interactive rendering; prerendering does not consume notifications. New messages arriving after the query remain unread. An already-open dashboard does not receive live updates automatically.

Migration: `20260909111631_AddMunicipalMessages`. It only adds the message table and its indexes and foreign keys. The existing startup migration mechanism applies it automatically. It was also applied to the local database after a SQLite backup in `App_Data`; all six existing applications were retained. Existing workspace edits, including the preceding address migration, were preserved.

## Automated verification

Run:

```powershell
dotnet build
dotnet run --project tests/MunicipalMessages/MunicipalMessages.csproj
```

The standalone integration runner uses actual Identity users, password validation, EF migrations, and a temporary file-backed SQLite database. It verifies:

- Existing applications survive upgrading from the preceding migration.
- Full multiline text exceeding 20,000 characters, sender and timestamps persist.
- Blank reviews do not create notifications.
- Wrong municipal officials and applicants cannot send messages.
- Owners get application-specific unread counts and chronological history.
- Another applicant cannot retrieve messages, see their notification counts, or mark them read.
- Reading displayed messages clears their counts while leaving concurrently arriving messages unread.
- Disposing all services and reopening the database preserves both unread and read messages and read state.

This is database/service integration testing, not a browser login test or an operating-system reboot. Browser automation was unavailable in the environment. The existing registration warning BL0008 remains; EF tools 10.0.7 also report being older than runtime 10.0.9. Test restore reported NU1900 because the NuGet vulnerability feed was unavailable, but execution passed.

## Remaining browser acceptance checks

1. Log in as a municipal official and open an application in their municipality.
2. Enter a multiline note in Officer Review Notes and save the review.
3. Log in as that application's owner. Verify the unread total and badge identify the application.
4. Open Track. Verify Municipal Messages shows the complete note, sender and UTC timestamp.
5. Return to the dashboard and verify the unread count decreases.
6. Send another note, stop and restart the app, and repeat the applicant checks. Previously read messages should remain visible.
7. Log in as a different applicant and navigate directly to the first applicant's tracking URL. Verify no application or message information appears.
8. Smoke-test application creation, municipal decisions, withdrawal/reapplication and PDF download in the browser; these existing flows were not redesigned.
