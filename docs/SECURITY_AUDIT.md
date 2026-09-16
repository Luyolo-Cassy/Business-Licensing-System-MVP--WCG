# Stage 7: security audit and regression verification

Completed 16 September 2026. Scope: the existing BusinessOwner, MunicipalOfficial and DEDATAdmin architecture, with minimal fixes. No roles, municipality schema, migrations, packages, geographic routing, or Official processing workflow were changed.

## Confirmed findings and fixes

1. **Public supporting documents.** Before the fix, an anonymous HTTP request for an existing `/uploads/...` document returned `200 application/pdf` with 14,995 bytes. The original static-asset manifest contained 234 upload routes, including fingerprinted variants. The probe used an isolated database and read an existing file without altering it.
   - New files now go directly to `App_Data/protected-uploads`.
   - Startup moves old files there, retaining filenames and database references. It validates source/destination containment, rejects links and collisions, and never overwrites files. A failed move prevents startup; already-moved files are not moved again on retry.
   - Middleware intercepts `/uploads/...` before static serving, including alternate/fingerprinted requests. A database application reference and current role/scope authorization are required. Files without a database reference return 404; unauthorized users cannot download them.
   - The project excludes legacy and private upload directories from static assets/publishing. The rebuilt manifest has zero upload routes. Public icons continue to work.
   - Downloads use attachment disposition, `private, no-store` and `nosniff`. They cannot execute historical uploaded HTML as inline application-origin content.
2. **Generated PDF ownership lacked an explicit applicant-role requirement.** Previously the owner ID alone granted access. Shared document authorization now requires current BusinessOwner membership for ownership access, current Official municipality access, or DEDATAdmin membership. It also rejects current lockout and mismatched security stamps. Removing an applicant's role denies both their PDF and supporting-document requests.
3. **Stale applicant status actions.** Withdrawal/reapplication callbacks wrote a previously loaded application without rechecking its current status. A stale page could overwrite a subsequent Official decision; reapplication also bypassed the active municipality gate. A small service now checks current identity, ownership, allowed status and, for reapplication, active supported municipality inside a fresh database scope/transaction. Normal owner withdrawal/reapplication is covered by tests. Unbound legacy applicant advance/reject methods were removed; no claim is made that those unused methods were remotely callable.

## Existing protections retained

- Admin pages have DEDATAdmin authorization, and management/oversight services independently check database role membership. Oversight services are read-only. All six Admin routes were tested directly.
- Official dashboard, reports and review use the current database Official assignment. Review/status/message writes use a fresh scope and transaction, rechecking assignment and security stamp. Cross-municipality IDs, stale reassignment and deactivation are rejected.
- Applicant tracking and messages filter by application ownership. Message read-state updates cannot affect another applicant or mark undisplayed concurrent messages as read.
- Official creation assigns one active municipality and only MunicipalOfficial. Admin supplies no password. Identity setup tokens, password rules, single-use completion, duplicate-user rejection and reactivation preserving the existing password remain intact.
- Municipality management is Admin-only. Active participation gates new assignments/submissions; renaming preserves routing identity and updates matching historical strings atomically. Deactivation preserves historical access and records. Geographic routing still supports only the original five municipalities.
- Identity role seeding and DevelopmentAdminSeeder remain. Runtime searches found no legacy Municipal Official email dependency. Restart tests verify that removed legacy users, roles or assignments are not recreated/restored.
- Normal HTTP self-registration was tested and assigns only BusinessOwner. Existing login destinations remain `/dashboard`, `/official-dashboard` and `/admin-dashboard`.

## Verified authorization matrix

| Operation | BusinessOwner | MunicipalOfficial | DEDATAdmin | Anonymous |
| --- | --- | --- | --- | --- |
| Applicant tracking/messages | Own applications only | Denied applicant route | Denied applicant route; oversight separately allowed | Denied |
| Supporting documents and generated PDFs | Own applications only | Current assigned municipality only | Province-wide | Denied |
| Official dashboard/reports/review | Denied | Current municipality only | Denied | Denied |
| Approve/reject/save review/messages | Denied | Current municipality only; stale/deactivated access denied | Denied | Denied |
| All six Admin routes | Denied | Denied | Allowed | Denied |
| Municipality/Official management services | Denied | Denied | Allowed | Denied |
| Withdrawal/reapplication | Own application with current eligible status; reapplication requires active participation | Denied | Denied | Denied |

The matrix describes the intended single-role accounts produced by existing workflows. Identity remains additive if an operator manually assigns multiple roles outside those workflows; no new mutually exclusive role system was introduced.

## Verification results

All automated tests used isolated temporary databases/content roots; routing tests use fake HTTP responses rather than real geographic APIs.

| Command | Result |
| --- | --- |
| `dotnet build` (permitted restore retry: `dotnet build --force`) | Passed, zero errors |
| `dotnet run --project tests/MunicipalMessages --no-restore` | Passed |
| `dotnet run --project tests/MunicipalityManagement --no-restore` | Passed |
| `dotnet run --project tests/MunicipalRouting --no-restore` | Passed |
| `dotnet run --no-build --project tests/OfficialManagement` | Passed |
| `dotnet run --no-build --project tests/AdminOversight` | Passed |
| `dotnet run --no-build --project tests/SecurityAudit` | Passed; 140 printed assertions plus exception-rejection checks |
| `dotnet ef migrations has-pending-model-changes --no-build` | No model changes |
| `dotnet ef migrations list --no-build` | All seven existing migrations applied; none pending |
| `git diff --check` | Passed |

Application startup and cookie-authenticated GET/POST flows were exercised by localhost test hosts. The focused suite also covers legacy document reference fields, private upload creation, idempotent migration, safe collision failure, orphan/fingerprint/traversal denial, role removal, reassignment/reactivation, preserved history, public icons and self-registration. Existing suites cover setup through the real anonymous form and stale processing actions.

Warnings/errors encountered:

- Existing BL0008 at `Components/Account/Pages/Register.razor:156` (form-bound property initializer). Normal registration passed. This unrelated warning remains.
- Sandboxed restore reported NU1900 because NuGet vulnerability data was unreachable. An approved network-enabled restore/build succeeded without NU1900.
- EF tools 10.0.7 report that runtime 10.0.9 is newer; both migration checks succeeded.
- HTTP-only test hosts report that no HTTPS redirect port is configured. The application HTTPS middleware was retained.
- Git reports LF-to-CRLF conversion notices; no whitespace errors.
- The initial tracking assertion incorrectly expected a business name the page does not render. It was corrected to the displayed application number; the complete rerun passed. No unresolved build/test errors remain.

## Existing development data

The explicit migration command `dotnet run --no-build --project tests/SecurityAudit -- --migrate-existing-uploads` moved **117** files from `wwwroot/uploads` to `App_Data/protected-uploads`. Every source/destination SHA-256 comparison passed. No document contents were deleted or overwritten. Existing application/document references were not updated.

Of these, **24 were Git-tracked**: Git therefore lists their old public locations as deleted. Their preserved private destinations are intentionally ignored. The remaining 93 were already untracked/ignored. A complete 117-file source/destination/hash inventory was written locally to `C:\Users\cabra\AppData\Local\Temp\stage7-upload-migration-inventory.csv` rather than committing private document metadata.

The development database was not reset, deleted, or written by the automated tests. Its SHA-256 before and after the file move/final checks was `0EEEAE8E1040C1C10ED3DDAAD98CBD4585B63A4E0EB0C7BB60CBB767289FE79F`. No users, applications, municipalities, messages, generated PDFs or migrations were deleted.

## Stage 7 file inventory

Created:

- `Services/ApplicationReadAccess.cs`
- `Services/ProtectedUploadService.cs`
- `Services/ApplicantApplicationService.cs`
- `tests/SecurityAudit/SecurityAudit.csproj`
- `tests/SecurityAudit/Program.cs`
- `docs/SECURITY_AUDIT.md`

Modified:

- `.gitignore`
- `Program.cs`
- `ProvincialBusinessLicensingSystem.csproj`
- `Components/Pages/NewApplication.razor`
- `Components/Pages/Tracking.razor`
- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/USER_GUIDE.md`

Moved: the 117 supporting files detailed above and in the local CSV. No source-code files were deleted. Build artifacts and isolated temporary test files are not source changes.

Prior-stage uncommitted navigation, Admin dashboard/oversight pages/service/tests, and OfficialManagement test changes were present before Stage 7 and preserved. They are not newly implemented Stage 7 features. Existing edits to Program/README/architecture/user guide were retained alongside the additions described here.

## Limits and manual browser checks

- This was code inspection plus automated service and cookie-authenticated HTTP testing, not an interactive browser run or external penetration test. Live Blazor clicks and rendered styling still warrant the manual checks below.
- Full interactive application submission and real geocoding were not replayed. Its geographic routing and active-municipality gate are unchanged; protected upload saving and routing are tested separately.
- Uploaded contents are not malware-scanned or signature-validated. This stage protects storage/access; it does not certify document contents.
- Previously public files may already have been downloaded or cached, and the 24 tracked documents remain in Git history. This change does not rewrite history or revoke copies already obtained.
- Deploy the rebuilt app and preserve/back up private storage separately. Do not leave copies of old uploads in an independently served web/proxy directory. Startup deliberately stops on a destination collision rather than risking data loss.
- The existing development-only Admin seed, disabled email-confirmation requirement and no-op email sender remain unchanged. Setup links are credentials and must continue to be shared privately.

Manual checks:

1. Register/sign in as an applicant; complete a normal routed submission, download each supporting document and generated PDF, and track messages.
2. Open a second applicant's tracking/document URLs and confirm denial. Repeat while signed out.
3. As Officials in two municipalities, verify review/report scope and cross-municipality document denial; approve/reject/send a message on an authorized application.
4. Leave an Official review page open, reassign/deactivate the account from Admin, and attempt a stale action. Confirm denial; reactivate and sign in again.
5. As Admin, inspect every oversight/management page and documents. Confirm Official processing routes remain denied and oversight has no processing controls.
6. Leave an applicant withdrawal/reapplication page open while its status changes; confirm the stale operation shows an error. Verify legitimate actions still work and inactive municipalities cannot receive a reapplication.

Stage 7 implementation and automated verification are complete; the manual browser checks above have not been claimed as performed.
