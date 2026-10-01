# ObjeX — AI Context

Self-hosted blob storage built with Clean Architecture in .NET 10.

---

## Project Layout

```
src/
├── ObjeX.Api/           # ASP.NET Core host — Program.cs (composition only), Startup/, Endpoints/, Middleware/, Auth/, Jobs/
│   ├── Endpoints/       # AccountEndpoints, DownloadEndpoints, PresignEndpoints
│   │   └── S3Endpoints/ # S3BucketEndpoint, S3ObjectEndpoint, S3MultipartEndpoint, S3PostObjectEndpoint
│   ├── Middleware/      # SigV4AuthMiddleware, SecurityHeadersMiddleware
│   ├── Auth/            # HangfireAuthorizationFilter
│   ├── Jobs/            # JobDefinitions (the recurring jobs: id, name, default cron, setting), HangfireJobMonitor (IJobMonitor: recurring jobs, recent runs, run details, run now, retry, delete, results in words), HangfireJobScheduler (IJobScheduler: save, reset, apply), JobCron (Cronos check)
│   ├── Options/         # ServerOptions (ports), ReverseProxyOptions, AuthOptions (lockout, RememberMeDays), DatabaseOptions, StorageOptions (blob root, upload cap, min free disk), SeedOptions
│   ├── Startup/         # ServiceCollectionExtensions (AddObjeX* per concern), DatabaseInitializer (migrate, pragmas, legacy blob paths, roles, admin, seeding), BackgroundJobs (Hangfire wiring, recurring schedule, stale-job prune)
│   ├── Components/      # App.razor (host document), _Imports.razor
│   ├── wwwroot/         # tokens.css (design tokens, the only file with colour values), app.css (fonts, document base, Radzen grid/dialog/notification styles), favicons, fonts/, site.webmanifest
│   ├── S3/              # S3Pipeline (the S3 port's request pipeline), SigV4Parser, SigV4Signer, S3Xml, S3Errors, S3Subresources (501 for unsupported ?acl, ?tagging, …), ObjectHeaders (stored x-amz-meta-* and system headers), Preconditions (conditional writes), ContinuationToken, CopySourceRange, LimitedStream (UploadPartCopy), S3RequestBody, AwsChunkedStream, ObjectDeletion, StorageQuota, ContentMd5
│   └── Metrics/         # ObjeXMetrics, BucketMetricsSyncJob
├── ObjeX.Core/          # Domain — zero framework dependencies
│   ├── Interfaces/      # IMetadataService, IObjectStorageService, IStorageQuotaService, IStorageSpaceService, IJobMonitor, IJobScheduler, IHashService, IHasTimestamps
│   ├── Models/          # Bucket, BlobObject, S3Credential, User, AuditEntry, ListObjectsResult, MultipartUpload, MultipartUploadPart, SystemSettings, JobSchedule
│   ├── Utilities/       # HashingStream (MD5 passthrough for ETag computation during upload), PresignedUrlGenerator, S3Conventions (region, addressing style), InlineMediaTypes (download/preview allowlist), ETags (multipart ETag detection)
│   └── Validation/      # BucketNameValidator (GetValidationError)
├── ObjeX.Infrastructure/
│   ├── Data/            # ObjeXDbContext (EF Core + SQLite, extends IdentityDbContext<User>)
│   ├── Hashing/         # Sha256HashService
│   ├── Health/          # BlobStorageHealthCheck
│   ├── Jobs/            # CleanupOrphanedBlobsJob, VerifyBlobIntegrityJob, CleanupAbandonedMultipartJob, RecountBucketStatsJob (Hangfire job classes)
│   ├── Metadata/        # EfCoreMetadataService (SQLite and PostgreSQL alike)
│   ├── Migrations/      # EF Core migrations
│   ├── Options/         # S3Options (PublicUrl), DefaultAdminOptions — here, not in Api, because Web needs them and cannot reference Api
│   └── Storage/         # FileSystemStorageService, StorageSpaceService (free disk of the blob volume), LegacyKeyPathMigration (moves pre-1.2.5 alias blobs to their raw-key path at startup)
├── ObjeX.Migrations.PostgreSql/  # PostgreSQL-specific EF Core migrations
├── ObjeX.Tests/         # xUnit — unit (Core validators, hashing) + integration (WebApplicationFactory, real SQLite, or PostgreSQL with OBJEX_TEST_POSTGRES)
│   ├── Unit/            # BucketNameValidator, ObjectKeyValidator, HashingStream, Sha256HashService, StorageSpaceStatus, ETags, CustomMetadata, InlineMediaTypes, S3ClientSnippets, TextPreview, BrowserTimeZone, CronText, CronPreset, JobCron, AppVersion, SearchPattern, UiRules (design rules, reads the UI sources as text), ThemeMode
│   └── Integration/     # S3 API round-trips, S3 conformance and pagination, auth, multipart, quotas, storage space, resilience, cookie auth, health, styleguide, background jobs and the Hangfire dashboard
└── ObjeX.Web/           # Razor class library: components, pages, dialogs, layout — no host, no wwwroot
    ├── Helpers/         # FileHelper, AppVersion, S3ClientSnippets, TextPreview, CustomMetadata, CronText (cron in words), CronPreset (daily and weekly presets ↔ cron), JobRunText
    ├── Services/        # ThemeMode (objex-theme cookie → Radzen theme and token mode class), BrowserTimeZone (the circuit's browser zone, set by Routes from the objex-tz cookie)
    └── Components/      # Routes, RedirectToLogin, S3ConnectSnippets
        ├── Pages/       # Dashboard, Buckets, Objects, Settings, Login, NotFound, Users, ChangePassword, AuditLog, Jobs, Error, Profile, Styleguide (Development only)
        ├── Dialogs/     # CreateBucketDialog, UploadObjectDialog, CreateS3CredentialDialog, ShowS3CredentialDialog, CreateFolderDialog, CreateUserDialog, ShowUserPasswordDialog, ChangeOwnerDialog, FilePreviewDialog, FileMetadataDialog, PresignedUrlDialog, S3ConnectDialog, ConfirmDialog, SetQuotaDialog, EditJobDialog
        ├── Layout/      # MainLayout, NavMenu, SidebarFooter, EmptyLayout, ReconnectModal
        └── Ui/          # the UI library: Ox* components with scoped CSS, OxEnums.cs (enums, OxSizes)
```

Outside `src/`: `tests/s3-conformance/` (harness that runs ceph/s3-tests against the checkout), `deploy/` (compose files, Helm chart), `docs/` (configuration, API, architecture).

`docs/architecture.md` holds the architecture, sequence, ER and state diagrams. Source is `docs/diagrams/objex.mmd` (one diagram per `%%% Title` section); `docs/diagrams/render.sh` regenerates the committed SVGs, `docs/diagrams/index.html` is a live viewer. Update the diagrams when a flow they show changes.

---

## Architecture Rules

- **ObjeX.Core** has zero framework/NuGet dependencies — only BCL. Keep it that way.
- **ObjeX.Infrastructure** implements Core interfaces. Never reference Api or Web. Internals are visible to `ObjeX.Tests` (`InternalsVisibleTo`).
- **ObjeX.Api** wires everything together. `Program.cs` only composes: typed options → `Startup/ServiceCollectionExtensions` (`AddObjeXDatabase/Storage/Identity/Blazor`, `AddObjeXBackgroundJobs`, `AddS3Api`) → `DatabaseInitializer` → pipeline. No business logic here.
- **ObjeX.Web** references both `ObjeX.Core` and `ObjeX.Infrastructure` (for `ObjeXDbContext` injection in Blazor components).
- New storage backends → implement `IObjectStorageService`. New metadata stores → implement `IMetadataService`. No other changes needed.

---

## Authentication & Authorization

### Overview — Dual Auth

ObjeX uses **two authentication mechanisms** operating independently:

| Mechanism | Scheme name | Used by |
|---|---|---|
| Cookie (ASP.NET Core Identity) | `Identity.Application` | Browser / Blazor UI |
| AWS Signature V4 | `"SigV4"` (custom middleware) | S3 clients on port 9000 |

The cookie is the default for the browser. S3 clients on port 9000 authenticate via AWS Sig V4 — no cookie, no `X-API-Key`.

### Ports and Pipelines

Ports come from `Server:UiPort` (default 9001) and `Server:S3Port` (default 9000) — `ObjeX.Api/Options/ServerOptions.cs`. Kestrel listeners are defined in code, so `ASPNETCORE_URLS` is ignored; do not set it anywhere. A request is dispatched by the TCP port it arrived on (`Connection.LocalPort`), never by the Host header — S3 clients sign the Host header, and behind a proxy it carries no port.

```
Shared (both ports)
  UseForwardedHeaders      ← only if ReverseProxy:Enabled (X-Forwarded-For/Proto from trusted proxies)
  UseSerilogRequestLogging
  UseHttpMetrics           ← only if Metrics:Enabled
  app.Use(...)             ← security headers (X-Content-Type-Options, X-Frame-Options, etc.)
  UseS3Api(s3Port)         ← terminal: S3-port requests enter the S3 pipeline and never continue below

S3 pipeline (ObjeX.Api/S3/S3Pipeline.cs — own ApplicationBuilder, own routing, invisible to the UI port)
  UseExceptionHandler      ← S3 XML InternalError 500
  UseCors("S3")            ← permissive CORS for S3 clients only; the UI port has no CORS (same-origin)
  SigV4AuthMiddleware      ← validates Sig V4, sets context.User; S3 XML error + short-circuit on failure
  UseRouting / UseAuthorization / MapGroup("/").RequireAuthorization() with the S3 endpoints
  Run                      ← fallback S3 XML 404 — nothing on this port answers with HTML or an empty body

UI pipeline (everything else)
  UseExceptionHandler      ← JSON 500 (Development: developer exception page)
  UseWhen(/styleguide)     ← outside Development only: plain 404, ahead of the status code pages
  UseWhen(!api, !metrics)  ← UseStatusCodePagesWithRedirects("/not-found") — only browser paths, never /api or /metrics
  UseResponseCompression
  UseStaticFiles
  UseRouting               ← explicit, so routing runs after the port split (WebApplication would otherwise insert it first)
  UseAuthentication        ← Identity cookie handler, sets context.User for cookie sessions
  UseAuthorization         ← enforces policies on the already-resolved context.User
  UseAntiforgery
  UseHangfireDashboard     ← Admin only (HangfireAuthorizationFilter), "Back to site" → /jobs
  health, metrics, Blazor, /api/*, /account/*
```

The S3 pipeline is a fresh `ApplicationBuilder`, not an `app.MapWhen` branch: a branch of `app` shares the global endpoint route builder, and UI endpoints would match inside it. Integration tests select the pipeline with the `X-ObjeX-Test-Port` header (see `ObjeXFactory`), because TestServer has no sockets and `LocalPort` is always 0.

### HTTP Security Headers

Set by `UseSecurityHeaders()` (`Middleware/SecurityHeadersMiddleware.cs`), in the part of the pipeline shared by both ports:

| Header | Value | Condition |
|--------|-------|-----------|
| `Server` | removed | `AddServerHeader = false` on Kestrel |
| `X-Powered-By` | removed | `ctx.Response.Headers.Remove(...)` |
| `X-Content-Type-Options` | `nosniff` | always |
| `X-Frame-Options` | `SAMEORIGIN` | always |
| `X-Permitted-Cross-Domain-Policies` | `none` | always |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | always |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` | non-dev only |

CSP is intentionally omitted — Blazor Server requires inline scripts and a SignalR WebSocket (`ws://`/`wss://`), making a safe policy non-trivial. Deferred.

`GET /api/objects/{bucket}/{*key}` applies its own media-type allowlist, because the stored `Content-Type` is chosen by whoever uploaded the object. Only the allowlist in `InlineMediaTypes` (`ObjeX.Core/Utilities`: `image/png|jpeg|gif|webp|avif|bmp|x-icon`, `video/*`, `audio/*`, `application/pdf`, `text/plain`, compared on the media type with parameters stripped) is served inline; the preview gate in `Objects.razor` and `FilePreviewDialog` reads the same list, plus the text types of `TextPreview.IsTextLike`, which the dialog loads through storage rather than the endpoint; everything else — `text/html`, `image/svg+xml`, `application/octet-stream` — becomes `application/octet-stream` with `Content-Disposition: attachment`. Inline responses also carry `Content-Security-Policy: sandbox`, except PDFs, which Chrome's viewer refuses to render in a sandboxed document and which cannot script the parent DOM anyway.

`SigV4AuthMiddleware` (`ObjeX.Api/Middleware/`) runs inside the S3 pipeline (`S3Pipeline.UseS3Api`), i.e. for every request arriving on `Server:S3Port`. It: parses the `Authorization: AWS4-HMAC-SHA256 ...` header (or presigned query params), looks up the `AccessKeyId` in `db.S3Credentials`, validates the HMAC-SHA256 signature, checks timestamp freshness (±15 min, presigned URLs use `X-Amz-Expires`), verifies the payload hash against `x-amz-content-sha256` — only that branch calls `EnableBuffering()`, so `UNSIGNED-PAYLOAD`, `STREAMING-*`, presigned and POST Object bodies are never spilled to a temp file — then sets `context.User` to a `ClaimsIdentity` with scheme `"SigV4"`. Returns S3 XML error responses on failure.

### 401 vs 302 for API Paths

By default, cookie auth challenges redirect to the login page (302). For API endpoints this is wrong — external clients expect 401. Two fixes are applied:

1. **`ConfigureApplicationCookie`** in `Startup/ServiceCollectionExtensions.AddObjeXIdentity` overrides `OnRedirectToLogin` and `OnRedirectToAccessDenied`: if `Request.Path.StartsWithSegments("/api")`, sets `StatusCode = 401` and returns without redirecting.

2. **`UseStatusCodePagesWithRedirects`** is wrapped in `app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), ...)` so it only intercepts non-API responses. Without this, the 401 would be caught by the status code middleware and redirected to `/not-found`, which then redirects to login.

### Authorization

No named policies are defined. S3 endpoints use `.RequireAuthorization()` on the route group (default policy = require authenticated user). Both auth mechanisms set `context.User` before `UseAuthorization` runs, so the default policy just checks `IsAuthenticated`.

### ASP.NET Core Identity Setup

- `User` model in `ObjeX.Core/Models/` extends `IdentityUser`
- `ObjeXDbContext` extends `IdentityDbContext<User>`
- Roles: `Admin`, `Manager`, `User` — all three seeded on every startup (idempotent). See role table below.
- Role hierarchy: Admin (1, permanent singleton) → Manager (0–N, promoted by Admin) → User (default)
  - **Admin**: full access, user management, role promotion, Settings incl. presigned URLs + storage quotas, Jobs page and Hangfire dashboard, all buckets, unlimited storage by default
  - **Manager**: Users page, Settings incl. presigned URLs + storage quotas, all buckets — cannot promote/demote roles, no Jobs page, unlimited storage by default
  - **User**: S3 credentials, dark mode, own buckets only, subject to global storage quota (configurable in Settings)
- Password requirements relaxed for MVP (min 4 chars, no complexity rules)
- Account lockout: `Auth:Lockout:MaxFailedAttempts` (default 5) failed logins lock the account for `Auth:Lockout:DurationMinutes` (default 5). Per account, failures only, enforced by Identity via `lockoutOnFailure: true` in `AccountEndpoints`. No IP-based rate limiting by design — CGNAT and shared proxies put many users behind one IP. A locked account shows as `Locked` on the Users page with an **Unlock** button (Admin and Manager, any row including the admin's own) that clears `LockoutEnd` and resets the failed-attempt count. `Auth:RememberMeDays` (default 30) is the cookie lifetime for logins that tick "Stay signed in".
- Email flows are no-ops — no `IEmailSender` registered, no email verification

**Default admin** (created by `DatabaseInitializer` when no user with the configured `DefaultAdmin:Username` exists; an existing user is never modified):
```
Username: admin  (or DefaultAdmin:Username in config)
Email:    admin@objex.local  (or DefaultAdmin:Email)
Password: admin  (or DefaultAdmin:Password)
```
When the configured password equals the built-in default, the admin is created with `MustChangePassword = true` and lands on `/change-password` after the first login. `TemporaryPasswordExpiresAt` stays null, so a fresh install cannot lock itself out. A password set via `DefaultAdmin:Password` is not forced. Integration tests that log in as `admin/admin` therefore get a redirect to `/change-password`, not to `returnUrl`.

⚠️ Change this in production via `appsettings.json` or environment variables.

### Login / Logout

Blazor Server cannot set HTTP cookies — the SignalR response is already committed by the time component code runs. Auth actions that touch cookies are therefore handled by **real HTTP endpoints**, not Blazor components:

```
POST /account/login   ← HTML form POST; sets Identity cookie; redirects to returnUrl or /
GET  /account/logout  ← clears Identity cookie; redirects to /login
```

The login endpoint accepts `login` (username or email — detected by `@` presence), `password`, and `returnUrl` form fields. On failure it redirects back to `/login?error=1&login={value}` so the form can pre-fill the username; `&msg=` carries a specific reason for locked, deactivated, or temporary-password-expired accounts. A ticked "Stay signed in" checkbox sends `rememberMe=true`. The endpoint checks the password with `CheckPasswordSignInAsync`, then calls `SignInAsync` with `IsPersistent = rememberMe` and `ExpiresUtc = now + Auth:RememberMeDays`. Unticked logins get a session cookie on the 60-minute sliding lifetime. Sliding renewal reuses the ticket's own lifetime (`ExpiresUtc - IssuedUtc`), so remembered sessions keep renewing at `RememberMeDays`. Every login POST also sets the `objex-remember` cookie (`1` or `0`, one year, HttpOnly) with the last choice; `Login.razor` reads it while prerendering and carries the value into the interactive render through `PersistentComponentState`, because `HttpContext` is null inside the circuit.

`Login.razor` uses `@layout EmptyLayout` and `[AllowAnonymous]`. It renders a plain HTML `<form method="post" action="/account/login">` — not a Blazor event handler. The fields are `OxTextInput` without `ValueChanged`, so they are plain form inputs and the password never travels over the circuit. It shows a Radzen toast notification on error (detected via `?error=1` query param in `OnAfterRenderAsync`).

### Blazor Global Route Protection

All pages using `MainLayout` are protected via `<AuthorizeView>` in `MainLayout.razor`. The `<Authorized>` branch renders the layout; `<NotAuthorized>` renders `<RedirectToLogin />`. `AuthorizeRouteView` alone is insufficient without per-page `[Authorize]` — `AuthorizeView` in the layout is the actual gate.

`RedirectToLogin.razor` calls `Navigation.NavigateTo("/login?returnUrl=...", forceLoad: true)` — `forceLoad: true` is required to escape the SignalR context and do a real page load.

`AddCascadingAuthenticationState()` is registered in DI (`Startup/ServiceCollectionExtensions.AddObjeXBlazor`). Do not use the `<CascadingAuthenticationState>` wrapper component — it cannot cascade to interactive children from a static SSR parent.

### S3Credential Model (`ObjeX.Core/Models/S3Credential.cs`)

```csharp
public class S3Credential : IHasTimestamps {
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string AccessKeyId { get; set; }      // "OBX" + 17 random uppercase alphanumeric (20 chars total)
    public required string SecretAccessKey { get; set; }  // 40 random bytes → base64url (~54 chars); stored plain — required for HMAC
    public required string UserId { get; set; }
    public User? User { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Credential generation:** use `S3Credential.Create(name, userId)` — returns `(S3Credential entity, string secretAccessKey)`. The secret is returned to the caller once (shown in UI) and stored in the DB plain — **this is intentional**: HMAC-SHA256 signing requires the original secret; a hashed version cannot be used for verification.

**Why plain storage:** AWS itself stores secret access keys in plain (or symmetrically encrypted) form. The security model is: protect the DB (encryption at rest, access control), not hash the secret. A hashed secret is incompatible with Sig V4.

EF Core `.ValueGeneratedNever()` on `Id`. Unique index on `AccessKeyId`.

### Hangfire Dashboard Auth

The Hangfire dashboard is mapped at `/hangfire` in every environment (`Program.cs`) for what the Jobs page does not cover (queues, servers, bulk actions). The Jobs page header links to it, a run's details page links to the run in it, and the dashboard's "Back to site" returns to `/jobs` (`AppPath`). An anonymous or non-admin request is refused, and the status code pages turn that into the `/not-found` redirect. `HangfireAuthorizationFilter` (`ObjeX.Api/Auth/`) requires `IsInRole("Admin")` on the cookie-authenticated user; there is no localhost bypass. Covered by `HangfireDashboardTests`.

---

## Background Jobs (Hangfire)

Hangfire is wired in `ObjeX.Api` only. Job classes live in `ObjeX.Infrastructure/Jobs/` — no job logic in the API layer.

**Packages (ObjeX.Api only):** `Hangfire.Core`, `Hangfire.AspNetCore`, `Hangfire.Storage.SQLite`, `Cronos` (Hangfire embeds it internally, so cron checks need the package itself)

**Storage:** Hangfire reuses the same `objex.db` SQLite file. Note: `Hangfire.Storage.SQLite` takes a **file path** (`/path/objex.db`), not an EF Core connection string (`Data Source=...`). `DatabaseOptions.SqliteFilePath` carries that absolute path; `BackgroundJobs.AddObjeXBackgroundJobs` (`Startup/`) passes it to `UseSQLiteStorage`, or uses `UsePostgreSqlStorage` for PostgreSQL.

**Recurring schedule and pruning:** `JobDefinitions` (`Api/Jobs/`) lists the four jobs below with id, name, default cron (UTC) and their setting, if they have one. `BackgroundJobs.RegisterRecurringJobs(app.Services)` declares each of them on its stored schedule (`job_schedules` row) or its default through `HangfireJobScheduler.Apply`, then removes every recurring job Hangfire still holds in storage that this version does not declare. Without that, a removed or renamed job class stays in storage and fails to load on every scheduler tick. A disabled job is declared with `Cron.Never()`, never removed, so its last run stays visible and Run now still works. A stored row this host cannot apply never stops the start: a zone the host does not know runs the cron in UTC, a cron Hangfire refuses runs the default schedule (a database moved from another host, or edited by hand). `HangfireJobScheduler.Resolve` decides this in one place for the start, the save and the monitor: `RecurringJobStatus.Cron` and `TimeZone` are the schedule Hangfire runs, `Warning` says why it differs from the stored row. The log, a warning icon in the Schedule column and an alert in `EditJobDialog` show it; Save is enabled right away so one click stores a schedule the host can run. Covered by `BackgroundJobsTests`.

**Schedules and settings:** the Admin edits a job on `/jobs` (Edit schedule in the row, `EditJobDialog`): enabled, daily or weekly with day and time, or a custom five-field cron, plus the job's setting. `IJobScheduler` (`ObjeX.Core/Interfaces`) is implemented by `HangfireJobScheduler` (singleton, `IDbContextFactory`). `SaveAsync` checks cron and zone with Cronos (`JobCron`; `ArgumentException` for an invalid or never-firing cron, an unknown zone or a setting out of range, nothing is stored), writes the `JobSchedule` row, the setting in `SystemSettings` (a value equal to the default is stored as null) and an `UpdateJobSchedule` audit entry with old → new in one `SaveChanges`, then calls `AddOrUpdate` with `RecurringJobOptions.TimeZone`. The dialog saves in the browser's zone (`BrowserTimeZone`), so "Sunday 04:00" stays 04:00 across daylight saving time; the defaults stay UTC. It fills the presets from the next run in the browser's zone (`CronPreset.Read`), and Save stays disabled until a field changes. `ResetAsync` deletes the row, clears the setting and writes `ResetJobSchedule`. The jobs read their setting from `SystemSettings` when they run. Covered by `JobSchedulerTests`, `JobMonitorScheduleTests`, `JobParameterTests`, `JobCronTests`, `CronPresetTests`.

**Retention:** `WithJobExpirationTimeout(BackgroundJobs.RunRetention)` in `AddObjeXBackgroundJobs` keeps succeeded and deleted runs 30 days instead of Hangfire's one day, so the Jobs page still shows the last run of a weekly job. Failed runs never expire.

**Jobs page:** `/jobs` (`Pages/Jobs.razor`, Admin only) replaces the dashboard. It reads `IJobMonitor` (`ObjeX.Core/Interfaces`, plain records), implemented by `HangfireJobMonitor` (`ObjeX.Api/Jobs/`, singleton from `AddObjeXBackgroundJobs`), because `ObjeX.Web` cannot reference Hangfire or `ObjeX.Api`. The monitor lists the jobs of `JobDefinitions` with their Hangfire entry and stored schedule (cron, zone, enabled, `IsDefault`, setting with default and range; a disabled job has no next run), collects run ids from `IMonitoringApi` (`ProcessingJobs`, `SucceededJobs`, `FailedJobs`) and reads every run from `JobDetails(id).History`, whose data the Hangfire state classes write themselves (the `StateData` of the list DTOs is null on SQLite). The job result is deserialized with `SerializationHelper` into its record and put in words (`HangfireJobMonitor.Describe`); a new job needs an entry in `JobDefinitions` and a case in `Describe`. "Run now" calls `IRecurringJobManager.Trigger`, also for a disabled job. Both tables start with Job and Status at the same widths and end with row actions, Details first (the last run, or the run); names and results are plain text. Recurring jobs: Job, Status, Schedule (`CronText`), Last run, Next run, no result column (the result is in Recent runs and behind Details). Recent runs: Job, Status, Started, Duration, Result. Recurring rows have Details, Edit schedule and a More menu (Run now, Reset to default); a disabled job shows the badge Disabled. Run rows have Details, Retry when possible and a More menu with Delete. Every run links to `/jobs/runs/{id}` (`Pages/JobRunDetailsPage.razor`, `IJobMonitor.GetRun`): summary (created from the oldest history entry, started, finished, duration, server, method), the result in words plus its JSON without Hangfire's `$type`, the exception type, message and stack trace, and the full state history. Failed and retrying runs have Retry (`IBackgroundJobClient.Requeue`); runs that are not running have Delete (`IBackgroundJobClient.Delete`). The recent runs list also holds retrying runs (`ScheduledJobs`). Display rules live in `Helpers/JobRunText`. A failed attempt that `AutomaticRetry` (default 10 attempts) reschedules shows as Retrying with the retry reason. The page reloads every 3 s while a run is queued or running. Covered by `JobMonitorTests`.

**DI registration:** `FileSystemStorageService` is registered as a singleton under its **concrete type first**, then aliased as `IObjectStorageService` (`ServiceCollectionExtensions.AddObjeXStorage`). This lets the job inject the concrete type directly (no cast) while the rest of the app uses the interface:
```csharp
services.AddSingleton(sp => new FileSystemStorageService(blobBasePath, ...));
services.AddSingleton<IObjectStorageService>(sp => sp.GetRequiredService<FileSystemStorageService>());
```

**Jobs:**

| Job class | Location | Schedule | Return type | What it does |
|---|---|---|---|---|
| `CleanupOrphanedBlobsJob` | `Infrastructure/Jobs/` | Default weekly Sun 03:00 UTC | `Task<CleanupResult>` | Derives the expected path of every object from bucket + key (never from the stored `StoragePath`, which goes stale when the data directory moves), scans `*.blob` files on disk, deletes any not in that set unless modified within the grace (`SystemSettings.OrphanGraceMinutes`, default 60, 15 to 10 080; an upload's blob exists before its row) |
| `VerifyBlobIntegrityJob` | `Infrastructure/Jobs/` | Default weekly Sun 04:00 UTC | `Task<IntegrityResult>` | Reads every blob file, recomputes MD5, compares against stored ETag — logs errors for corrupted or missing blobs. Multipart objects (`ETags.IsMultipart`, ETag carries `-N`) count as `Skipped` and are never hashed, because their ETag is the MD5 of the part MD5s; a missing blob is still reported for them |
| `CleanupAbandonedMultipartJob` | `Infrastructure/Jobs/` | Default weekly Sun 05:00 UTC | `Task<AbandonedMultipartResult>` | Deletes multipart uploads older than `SystemSettings.AbandonedMultipartDays` (default 7, 1 to 365; DB rows + part files on disk), also removes orphaned `_multipart` directories |
| `RecountBucketStatsJob` | `Infrastructure/Jobs/` | Default weekly Sun 06:00 UTC | `Task<RecountResult>` | Compares ObjectCount and TotalSize of every bucket with its objects in one query and recounts only the buckets that drifted, through `UpdateBucketStatsAsync`; logs a warning per corrected bucket |

`CleanupResult` (record, defined in same file): `FilesChecked`, `FilesDeleted`, `DurationSeconds`, `Timestamp`.
`IntegrityResult` (record, defined in same file): `Checked`, `Corrupted`, `Missing`, `Skipped`, `DurationSeconds`, `Timestamp`.
`AbandonedMultipartResult` (record, defined in same file): `UploadsChecked`, `UploadsDeleted`, `DurationSeconds`, `Timestamp`.
`RecountResult` (record, defined in same file): `BucketsChecked`, `BucketsCorrected`, `DurationSeconds`, `Timestamp`. Returning a value from the job method makes the result visible on the Jobs page.

`FileSystemStorageService.BasePath` is `internal` — accessible to jobs in the same `ObjeX.Infrastructure` assembly, not visible outside.

---

## Core Interfaces

```csharp
// ObjeX.Core/Interfaces/IObjectStorageService.cs
public interface IObjectStorageService
{
    Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default); // stage + commit
    Task<IStagedBlob> StageAsync(string bucketName, string key, Stream data, CancellationToken ctk = default);
    Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default);
    Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, CancellationToken ctk = default); // removes the bucket's blob folder; called after the rows are gone
    Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default);
}

// ObjeX.Core/Interfaces/IStagedBlob.cs
public interface IStagedBlob : IAsyncDisposable
{
    long Size { get; }                                          // bytes written to the temp file
    Task<string> CommitAsync(CancellationToken ctk = default);  // moves the temp file into place, returns the storage path
}
// DisposeAsync without a commit deletes the temp file; the object under the key keeps its old bytes.

// ObjeX.Core/Interfaces/IMetadataService.cs
public interface IMetadataService
{
    Task<Bucket> CreateBucketAsync(Bucket bucket, string? auditUserId = null, CancellationToken ctk = default);
    Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default);
    Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, string? auditUserId = null, CancellationToken ctk = default);
    // ownerFilter: null = no filter (Admin/Manager), userId = restrict to owner (User role)
    // isPrivileged: true = skip ownership check on delete (Admin/Manager bypass)
    // auditUserId: when non-null, writes an AuditEntry in the same transaction as the mutation
    Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default);
    Task<BlobObject> SaveObjectAsync(BlobObject blobObject, string? auditUserId = null, CancellationToken ctk = default);
    Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, string? startAfter = null, int? maxKeys = null, CancellationToken ctk = default);
    // Keys in UTF-8 byte order: ORDER BY key, COLLATE "C" on PostgreSQL (decided by Database.ProviderName), also for the key > startAfter seek.
    // Reads 1000 rows per query; after a common prefix it jumps past all its keys (prefix with the last char + 1), so a folder of any size costs one row.
    // startAfter is exclusive and skips a common prefix at or before it; maxKeys counts objects plus prefixes, null = everything (UI, ZIP)
    // Search term: NFC and NFD spellings match alike (keys stay byte for byte); * = any run of characters, ? = exactly one; %, _ and \ stay literal. No wildcard = matches anywhere; with a wildcard = anchored at the end (*.pdf excludes a.pdfx). Translation in Infrastructure/Metadata/SearchPattern.cs
    Task<IReadOnlyList<BlobObject>> SearchObjectsAsync(string bucketName, string? prefix, string term, int limit, CancellationToken ctk = default);
    // keys under prefix, case-insensitive, placeholders excluded, byte order, at most limit
    Task<IReadOnlyList<BlobObject>> SearchAllObjectsAsync(string? ownerFilter, string term, int limit, CancellationToken ctk = default);
    // same term semantics across every bucket owned by ownerFilter (null = all); ordered by bucket then key, provider-aware collation
    Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default); // all objects across all buckets — NOT filtered, used by Hangfire cleanup
    Task DeleteObjectAsync(string bucketName, string key, string? auditUserId = null, CancellationToken ctk = default);
    Task<int> DeleteObjectsAsync(string bucketName, IEnumerable<string> keys, string? auditUserId = null, CancellationToken ctk = default);
    // DeleteObjectsAsync: one transaction, one stats update, one DeleteObject audit entry per deleted key; unknown keys are ignored (S3 semantics), returns rows deleted
    Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default);
    // UpdateBucketStatsAsync is the full recount in one UPDATE with subqueries, for repair only (RecountBucketStatsJob). SaveObjectAsync/DeleteObjectAsync/DeleteObjectsAsync adjust ObjectCount and TotalSize
    // by the changed object's delta via ExecuteUpdate in the same transaction as the row change; overwrite = count unchanged, size delta = new - old.
}

// ObjeX.Core/Models/ListObjectsResult.cs
public record ListObjectsResult(IEnumerable<BlobObject> Objects, IEnumerable<string> CommonPrefixes, bool IsTruncated = false, string? NextMarker = null);
// Objects = files at current level; CommonPrefixes = virtual folder paths (e.g. "photos/2024/")
// Placeholder objects (key ends with "/", ContentType "application/x-directory") are filtered from UI; S3 listings return every key, so clients can delete them

// ObjeX.Core/Interfaces/IStorageQuotaService.cs
public record StorageQuotaStatus(long UsedBytes, long? QuotaBytes); // HasQuota, UsedPercent
public interface IStorageQuotaService
{
    // Used = size of the user's buckets. Quota = per-user value, else the global default for the User role, else null (unlimited).
    // The S3 507 check (Api/S3/StorageQuota) resolves the bucket's OwnerId and calls GetAsync(ownerId), never the caller: an Admin or Manager
    // uploading into a user's bucket is bound by that user's quota. An overwrite is charged newSize - existingSize, floored at zero.
    // The Dashboard's "My Storage" card uses the same rule; the Users page applies it in one query for all users.
    Task<StorageQuotaStatus> GetAsync(string userId, CancellationToken ctk = default);
}

// ObjeX.Core/Interfaces/IStorageSpaceService.cs
public record StorageSpaceStatus(long FreeBytes, long TotalBytes, long MinimumFreeBytes); // IsBelowMinimum, IsNearMinimum (<= 2x minimum), UsedBytes, UsedPercent
public interface IStorageSpaceService
{
    // DriveInfo of the blob root plus Storage:MinimumFreeDiskBytes, the threshold below which the S3 upload path answers 507.
    // Singleton from AddObjeXStorage; FileSystemStorageService.GetAvailableFreeSpace() delegates here, so one class reads the drive.
    StorageSpaceStatus Get();
}

// ObjeX.Core/Interfaces/IHashService.cs
public interface IHashService
{
    string ComputeHash(string input); // returns 64-char lowercase hex string
}
```

---

## Models

```csharp
// Bucket: Id (Guid), Name, OwnerId (FK → AspNetUsers, Restrict), Owner? (nav), ObjectCount, TotalSize, Objects (nav), CreatedAt, UpdatedAt
// BlobObject: Id (Guid), BucketName, Key, Size, ContentType, ETag, StoragePath, CustomMetadata (JSON), Bucket (nav), CreatedAt, UpdatedAt
// S3Credential: Id (Guid), Name, AccessKeyId, SecretAccessKey (plain), UserId, User (nav), LastUsedAt, CreatedAt, UpdatedAt
// AuditEntry: Id (long autoincrement), UserId (required), Action (required), BucketName?, Key?, Details?, Timestamp (DateTime.UtcNow default)
// SystemSettings: Id (always 1), PresignedUrlDefaultExpirySeconds, PresignedUrlMaxExpirySeconds, DefaultStorageQuotaBytes?, OrphanGraceMinutes?, AbandonedMultipartDays? (null = default)
// JobSchedule: JobId (PK, recurring job id), Cron (five fields), TimeZone (the zone the cron is read in), Enabled, CreatedAt, UpdatedAt — no row = default schedule
// User: extends IdentityUser — adds StorageUsedBytes, StorageQuotaBytes (nullable, per-user override), IsDeactivated, MustChangePassword, TemporaryPasswordExpiresAt, CreatedAt, UpdatedAt
// All implement IHasTimestamps (User via explicit properties; AuditEntry does not — immutable append-only)
```

---

## Conventions

- **DB columns**: snake_case via `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`)
- **JSON responses**: camelCase, nulls omitted (`JsonNamingPolicy.CamelCase`, `WhenWritingNull`)
- **EF migrations**: run automatically on startup via `db.Database.Migrate()` in `Startup/DatabaseInitializer.cs` (disable with `Database:AutoMigrate=false`)
- **Bucket name rules**: 3–63 chars, lowercase letters, digits, hyphens and periods, starting and ending with a letter or digit; no `..`, no period next to a hyphen, not an IP address — enforced by `BucketNameValidator`
- **Object keys**: support slashes (virtual paths). Validated by `ObjectKeyValidator.GetValidationError` (in `ObjeX.Core/Validation/`) — rejects empty, >1024 chars, leading `/` and control characters (including null bytes). `..` and `\` are ordinary key characters. `FileSystemStorageService` hashes the raw key, never a normalised form, so two distinct keys never share a blob; the logical key is stored as-is in DB, the physical path is always a SHA256 hash
- **ETag**: MD5 of the uploaded stream, hex-encoded lowercase

---

## Startup Seeding

`Startup/DatabaseInitializer.cs` seeds buckets and S3 credentials from config on startup (typed `SeedOptions`; idempotent, skipped if already exists). All seeded resources are owned by the default admin.

| Config key | Env var | Effect |
|---|---|---|
| `Seed:Buckets` | `Seed__Buckets` | Comma-separated bucket names to create |
| `Seed:S3Credential:AccessKeyId` | `Seed__S3Credential__AccessKeyId` | User-chosen access key |
| `Seed:S3Credential:SecretAccessKey` | `Seed__S3Credential__SecretAccessKey` | User-chosen secret key |
| `Seed:S3Credential:Name` | `Seed__S3Credential__Name` | Display name (default: `seed-credential`) |

Empty or unset values are no-ops. Invalid bucket names are logged and skipped. See `deploy/docker-compose.yml` for usage example.

---

## Storage Paths

- **Database**: `data/db/objex.db` (set in `ConnectionStrings:DefaultConnection`; hardcoded default when absent from config)
- **Blob storage**: `data/blobs` (set in `Storage:BasePath`; hardcoded default when absent from config)
- **Logs**: `data/logs/objex-.log` (Serilog file sink in `appsettings.json`)

Relative paths resolve against the **content root** via `ResolvePath()` in `Program.cs` — the project directory under `dotnet run` (so `src/ObjeX.Api/data/`), `/app` in the container. Never against the process working directory: that used to leave a second `data/` behind whenever the app was started from a different shell location. `launchSettings.json` must not set `workingDirectory` (`dotnet run` ignores it anyway). Deployed instances should still configure absolute paths.

### Content-Addressable Blob Layout

Physical blob paths are **derived from a SHA256 hash of `"{bucketName}/{key}"`**, not from the key string itself.

```
{basePath}/{bucketName}/{L1}/{L2}/{hash}.blob

L1 = hash[0..1]   (first 2 hex chars)
L2 = hash[2..3]   (next  2 hex chars)

Example:
  bucket = "photos", key = "2024/trip.jpg"
  hash   = sha256("photos/2024/trip.jpg") = "a3f7c2..."
  path   = /data/blobs/photos/a3/f7/a3f7c2....blob
```

**Staged writes:** `StageAsync` writes `{hash}.blob.{guid}.tmp` and returns an `IStagedBlob`. PUT object, UploadPart and POST Object run the `Content-MD5` check and the post-write quota check between stage and commit; an early return disposes the staged blob, and the previous object keeps its bytes and its metadata row. `CommitAsync` is the `File.Move(..., overwrite: true)`, atomic on Linux. `StoreAsync` is stage plus commit; CopyObject and the Blazor upload still use it, because their quota pre-check is exact. Parts follow the same pattern through `FileSystemStorageService.StagePartAsync`, which also yields the part ETag. On crash the `.tmp` file is cleaned up at next startup (files older than 1 hour are deleted).

**Why hashed paths:**
- Eliminates path traversal risk — the logical key never touches the filesystem raw
- Distributes files evenly across 256×256 = 65,536 directories — no hot directories
- Decouples the public key namespace from the physical layout entirely

---

## Blazor UI Architecture

**Hosting model:** Blazor Server (InteractiveServer), not WASM.

**Combined host:** `ObjeX.Api` is the single process — it serves both the REST API and the Blazor UI. `ObjeX.Web` is a Razor class library (`Microsoft.NET.Sdk.Razor`) holding components, pages, dialogs and layout; it has no entry point and no `wwwroot`. The host document `App.razor`, `wwwroot` (app.css, favicons, fonts) and `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(Routes).Assembly)` live in `ObjeX.Api`. Assets that ship inside the class library — collocated `*.razor.js` modules and scoped CSS — are served under `_content/ObjeX.Web/...`; JS interop imports and `@Assets[...]` references in Web components must use that prefix. Scoped CSS of the library is folded into the host bundle `ObjeX.Api.styles.css`.

**Data access from Blazor:** Components inject Core interfaces (`IMetadataService`) or `IDbContextFactory<ObjeXDbContext>` — no HttpClient, no API calls. Pages take the factory, never the scoped `ObjeXDbContext`, because a scoped context would live as long as the SignalR circuit. Each operation opens its own `await using var db = await DbFactory.CreateDbContextAsync()`; an entity read in one operation is detached by the next, so re-query it (or let `Remove`/`Update` attach it) before saving on a new context.

`AddObjeXDatabase` registers `AddDbContextFactory<ObjeXDbContext>`, which also registers `ObjeXDbContext` as scoped — that scoped context still serves Identity, `SigV4AuthMiddleware` and the endpoints. Mutations of `User` on the Users page therefore go through `UserManager` (Identity's scoped context), reads through the factory; mixing them would let a stale tracked `User` overwrite fresh columns, since `UserStore.UpdateAsync` marks every property modified. `EfCoreMetadataService` opens its own short context per call through the factory: a context shared for a whole circuit would track every object it wrote, and a later overwrite or delete would work on stale values or throw (`MetadataScopeTests`). Work that runs beside the circuit — the Dashboard timer, the layout's account check — takes its own scope or factory context too.

```
Browser → SignalR → Blazor Server (ObjeX.Api process)
                         ↓
      IMetadataService / IDbContextFactory<ObjeXDbContext>
                         ↓
                   EfCoreMetadataService / EF Core

External S3 clients → HTTP → ObjeX.Api endpoints → same services
```

**Render mode:** Set globally on `<Routes @rendermode="InteractiveServer" />` in `ObjeX.Api/Components/App.razor`. Do NOT add `@rendermode` per-page — the global setting covers all pages.

**Layout:** `MainLayout` renders `OxShell`: sidebar with `NavMenu` (Dashboard, Buckets, Audit Log, Users, Jobs, Settings; Audit Log and Jobs for the Admin only) and `SidebarFooter` (disk meter, user, sign out, version). Below 900 px the sidebar is a drawer. Page content starts with `OxPageHeader`; its title or `OxBreadcrumbs` is the `<h1>` that `FocusOnNavigate` targets.

**UI library:** `ObjeX.Web/Components/Ui/`, style "Papier". Pages, dialogs and layout use only `Ox*` components plus the Radzen components that stay: `RadzenDataGrid`, charts, `RadzenComponents` (dialog, notification, context menu, tooltip), `RadzenDropDown`, `RadzenNumeric`. No `RadzenStack`, `RadzenText`, `RadzenButton`, `RadzenCard`, `RadzenLayout`, `RadzenSidebar`, `RadzenPanelMenu`, `RadzenBadge`, `RadzenFormField`. When a value or a component is missing, add it to the tokens or to `Ui/` first, then use it. Radzen is registered via `AddRadzenComponents()` in `Startup/ServiceCollectionExtensions.AddObjeXBlazor`. `<RadzenComponents />` in `MainLayout.razor` hosts dialog, notification, context menu, tooltip and chart tooltip. Do not add a separate `<RadzenDialog />`, `<RadzenNotification />` or `<RadzenContextMenu />` next to it — each host subscribes to the service unguarded, so a second one renders every popup twice. `EmptyLayout` (login, change password, styleguide) has its own `<RadzenDialog />` and `<RadzenNotification />`, because it never renders together with `MainLayout`.

**Design tokens:** `ObjeX.Api/wwwroot/tokens.css`, three layers. Layer 1 is base values: the palette (`--ox-paper-*`, `--ox-teal-*`, `--ox-viz-*`) and the scales for font size, space, radius and control size. Layer 2 is semantic roles (`--ox-surface`, `--ox-text`, `--ox-line`, `--ox-accent`, `--ox-chart-1…8`, …), one set on `:root, .ox-light` and one on `.ox-dark`. Layer 3 derives the `--rz-*` variables from the roles, so Radzen follows the tokens. Components read layer 2 and the scales only. Colour values exist in `tokens.css` only. `App.razor` loads `<RadzenTheme>`, then `tokens.css`, then `app.css`, then the scoped CSS bundle; the order decides. Every `Ox*` component has a scoped `.razor.css`; styles that reach into Radzen markup from outside (grid rows, row actions on hover, dialog, notification) are global in `app.css`.

**Ui rules, enforced by `UiRulesTests`:** no `style="…"` attribute and no `<style>` block in a `.razor` file outside `Components/Ui/`; no hex, `rgb()` or `hsl()` value in `.razor`, `.css` or `.razor.js` outside `tokens.css`; no Radzen layout component; every other Radzen component must be on the allow list in the test. Sizes a page hands to a Radzen parameter (column widths, dialog widths) are constants in `OxSizes`. A control gets a width through `OxBox`.

**Styleguide:** `/styleguide` renders every `Ui/` component in light and dark side by side (`OxThemeScope`). Development only: outside Development the host answers 404 (`Program.cs`, ahead of the status code pages) and the page calls `NavigationManager.NotFound()`. No page links to it.

**Validation pattern:**
- **Enforcement** → service layer only (`EfCoreMetadataService` calls `BucketNameValidator`, throws `ArgumentException` on invalid input)
- **UX feedback** → Blazor dialogs use the same `BucketNameValidator` from Core for inline errors as the user types
- **API endpoints** → do NOT duplicate validation; catch `ArgumentException` from the service and return `400 BadRequest`

**Input reactivity:** `OxTextInput` and `OxSearch` wrap a native `<input>` and fire `ValueChanged` on every keystroke (`@bind-Value` works). `OxTextInput` attaches its handlers only when someone listens: a handler that ignores the event would make Blazor reset the input to its rendered value. Without `ValueChanged` it is a plain form field (login).

**EF Core + `init` properties:** Both `Bucket` and `BlobObject` use `Guid Id { get; init; } = Guid.NewGuid()`. EF Core 10 must be told not to generate its own value — both entities have `.ValueGeneratedNever()` configured in `ObjeXDbContext`. Do not remove this — removing it causes "Unexpected entry.EntityState: Detached" on insert. Same applies to `S3Credential.Id`.

**Dialogs:** Use `DialogService.OpenAsync<TComponent>("Title")` — returns the value passed to `DialogService.Close(value)`, or `null` if cancelled. Always null-check the return before acting on it. For complex return types, define a nested `public record` inside the dialog's `@code` block and reference it as `DialogComponent.RecordType` from the caller. Use `OpenAsync` (not `Alert`) when the dialog body needs rendered HTML — `Alert` renders plain text only.

Keyboard handling: text-input dialogs (`CreateBucketDialog`, `CreateS3CredentialDialog`, `CreateFolderDialog`, `CreateUserDialog`) use `OnEnter` and `OnEscape` of `OxTextInput` — Enter submits (if valid), Escape cancels. `ShowS3CredentialDialog` binds `@onkeydown` on the container `<OxStack tabindex="-1">`. Yes-or-no questions go through `ConfirmDialog.AskAsync(DialogService, title, message, confirmText, danger)`, not `DialogService.Confirm`; dialog widths come from `OxSizes.Dialog*`. Do NOT rely on Radzen's built-in Enter-to-submit — it doesn't exist.

`FileMetadataDialog` renders the stored `x-amz-meta-*` headers as a "Custom metadata" section via `Helpers/CustomMetadata.Parse` — prefix stripped, ordinal key order, malformed or empty JSON treated as no entries.

`ShowS3CredentialDialog` displays both `AccessKeyId` and `SecretAccessKey` with copy-to-clipboard buttons. Shows a warning alert: "Save your secret access key now — it won't be shown again." The dialog has `CloseDialogOnOverlayClick = false, ShowClose = false` — user must click Done.

**File downloads are the exception to "no API calls from Blazor":** Blazor Server runs on the server and cannot push file bytes to the browser's download manager through SignalR. Download buttons use a plain `<a href="/api/objects/..." download>` pointing at the API endpoint. This is not an architecture violation — it's a browser constraint.

**Clickable links in grids:** `OxFileName` (icon plus name, a link when `Href` is set) for buckets, folders and objects; `OxLink` for any other link. Row actions sit in `OxRowActions`: at most two direct `OxIconButton`s and one `OxMenuButton` with `OxMenuItem`s for the rest, destructive entries last. They show on row hover and keyboard focus, always on touch devices. A selected row gets `OxSizes.SelectedRowClass` through `RowRender`.

**Virtual folder navigation:** `Objects.razor` tracks `_currentPrefix` (e.g. `"photos/2024/"`) as component state. Calls `ListObjectsAsync` with `delimiter: "/"` — folders render as links, files as regular rows in a unified `RadzenDataGrid`. Breadcrumb segments and folder names are real links (`?prefix=`); the router keeps the circuit and `OnParametersSetAsync` loads the folder, so Back and Forward work. Folder create writes a zero-byte placeholder object with key `prefix/` and `ContentType: application/x-directory`. Upload prepends `_currentPrefix` to the file name. Placeholder objects (key ends with `/`) are filtered from file rows. File rows show Download and Share link; the More menu holds Preview, Metadata, "Copy key", "Copy S3 URI" (`s3://{bucket}/{key}`) and Delete. Folder rows have the menu only (Download ZIP, Delete). Copy feedback is a Success notification. While at least one row is selected, `OxSelectionBar` ("N selected", Download ZIP, Delete, clear) takes the place of New folder and Upload on the header line, so selecting never moves the table; search and an upload's Cancel stay. The "Modified" column shows `UpdatedAt`. A search box in the toolbar (hidden while the bucket is empty) filters with a 300 ms debounce over `_currentPrefix` and everything below it — the grid then shows file rows only, keyed relative to the prefix, capped at 500 with a "N results · first 500 shown" caption; Escape, the clear button and breadcrumb navigation restore the folder view.

**Disk space:** free of total space of the blob volume via `IStorageSpaceService`, in the sidebar footer (`SidebarFooter.razor`) for every role, because the 507 hits every role; re-read on each navigation. The meter turns `Warning` at or below twice `Storage:MinimumFreeDiskBytes` and `Danger` at or below it. The Dashboard has no Disk card; it shows an alert in those two states. The disk read in `LoadStats` has its own change label, because free space moves without any bucket changing.

**Dashboard charts:** `RadzenChart` inside `OxChart`. Slices take `OxChart.Slot(n)` (`--ox-chart-1…8`, then `--ox-chart-other`) with a 2 px surface stroke as the gap. A bucket keeps its colour in both bucket charts: the slot comes from its rank by size. The chart colours are validated as a set for colour-blind separation and 3:1 against the surface; do not add a ninth hue.

**Global search on `/buckets`:** `Buckets.razor` has a 300 ms debounced search box (hidden when the user has no buckets) that calls `SearchAllObjectsAsync(_isPrivileged ? null : userId, term, 501)` and swaps the bucket grid for a result grid — Bucket link, Key linking to its folder (`?prefix=`, per-segment encoded), Size, Modified, download; Escape or the clear button restores the bucket grid.

**Dark mode:** Theme stored in the `objex-theme` cookie (`standard` or `standard-dark`; `ThemeMode` also reads the older `material` and `material-dark`). `App.razor` reads the cookie via `IHttpContextAccessor` server-side, passes `ThemeMode.Radzen(...)` to `<RadzenTheme>` and sets `ox-light` or `ox-dark` on `<html>` — no flash on load. An inline `<script>` in `<head>` sets the cookie from `prefers-color-scheme` on the first visit and reloads once when that is dark. The toggle in Settings uses `ThemeService.SetTheme()`, a JS cookie write and `ObjeX.setTheme()` for the class on `<html>`. `ThemeService` is registered as `AddScoped<ThemeService>()` — do NOT use `AddRadzenCookieThemeService` (it fights the server-side rendering). Read the initial switch state from the cookie via JS in `OnAfterRenderAsync`, not from `ThemeService.Theme` (which is null on Blazor init).

**Time zone:** All stored timestamps are UTC; the browser's zone comes from the `objex-tz` cookie. An inline `<script>` in `App.razor` writes it on every load (`Intl.DateTimeFormat().resolvedOptions().timeZone`), unconditionally, because a laptop may change zones. `App.razor` reads the cookie via `IHttpContextAccessor` and passes it as `<Routes TimeZoneId="..." />`; `Routes.OnParametersSet` calls `BrowserTimeZone.Set()`. `BrowserTimeZone` is scoped, registered in `AddObjeXBlazor` next to `ThemeService`, so it lives as long as the circuit. Pages and dialogs inject `BrowserTimeZone Tz` and render `Tz.Format(...)` (`yyyy-MM-dd HH:mm`, invariant culture) or `Tz.FormatSeconds(...)` only where seconds were already shown. Never use `ToLocalTime()` or `DateTime.Now` in the UI: both give the server's zone. `ToLocal` stamps `DateTimeKind.Utc` first, because SQLite returns `Kind == Unspecified`. An unknown, empty or over-long id falls back to UTC; `UnknownId` keeps a non-empty id the server does not know, and `EditJobDialog` warns with it instead of silently saving in UTC. The first server render before the cookie exists is UTC; the login redirect is a full page load, so every page after login carries the cookie. The `mcr.microsoft.com/dotnet/aspnet:10.0` base image (Ubuntu 24.04) ships tzdata; an Alpine or chiseled base would need it added.

**Font:** Inter, self-hosted in `ObjeX.Api/wwwroot/fonts/` (weights 400, 500, 600 are declared). Set on `body` and as `--rz-text-font-family` through `--ox-font`. Icons are Material Symbols Outlined from the font Radzen ships (`--ox-font-icon`), rendered by `OxIcon`; no second icon set.

**Theme colors:** teal `--ox-accent` for the primary action, warm greys for everything else, colour on the folder, image and video icons. All of it is in `tokens.css`; the Radzen base theme is `standard` / `standard-dark`.

**Server-side paging with Radzen + prerender:** Radzen DataGrid's `LoadData` event does NOT fire after the interactive WebSocket reconnects — the prerender phase consumes the initial trigger. Fix: call `_grid.Reload()` in `OnAfterRenderAsync(firstRender)`. See `AuditLog.razor` for the pattern.

**Profile page** (`/profile`): username (alphanumeric only, no spaces, validated per-keystroke via `@oninput`), email, and password change sections. Error messages use `OxField` with `ReserveError`, which keeps the line so the form does not jump. After username save: `forceLoad: true` reload to refresh NavMenu. After password change: forced logout (`Navigation.NavigateTo("/account/logout", forceLoad: true)`).

---

## Endpoint Routes

```
# Internal endpoints — port 9001 (used by Blazor UI, cookie auth)
GET    /api/objects/{bucket}/{*key}          → download object (browser file download); x-objex-verify-integrity re-hashes, multipart objects skip the check
GET    /api/objects/{bucket}/download        → ZIP download; accepts ?prefix= to scope to a virtual folder; entry names drop empty, `.` and `..` segments, `\` splits like `/` (no zip slip)

# Auth (no auth required)
POST   /account/login     → form login (sets cookie), redirects to returnUrl
GET    /account/logout    → clears cookie, redirects to /login

# System
GET    /health            → liveness (200 if process is up, no checks); also at /health/live
GET    /health/ready      → readiness (checks DB connectivity + blob storage writability)
GET    /metrics           → Prometheus metrics (HTTP request stats + per-bucket storage gauges, synced every 30s, deleted buckets dropped); open unless Metrics:Token is set (Bearer)
GET    /audit             → Audit log (Admin only); server-side paginated table of bucket/object operations
GET    /jobs              → Jobs page (Admin only); recurring jobs, recent runs, run now, edit schedule and setting, reset, retry, delete
GET    /jobs/runs/{id}    → one run: summary, result, error with stack trace, state history
GET    /hangfire          → Hangfire dashboard (Admin role); anonymous and other roles are redirected to /not-found
GET    /styleguide        → Ui library styleguide (Development only, 404 otherwise)

# S3-Compatible API — Server:S3Port, default 9000 (AWS Signature V4 required)
# Own pipeline (ObjeX.Api/S3/S3Pipeline.cs): MapGroup("/").RequireAuthorization() inside its own routing.
# Selected by TCP port, no RequireHost — the Host header is free (proxies, ingresses, tunnels).
# Auth: SigV4AuthMiddleware runs before UseAuthorization, sets context.User on valid signature
GET    /                        → list all buckets (S3 ListAllMyBuckets XML); prefix, max-buckets (1–10000), continuation-token
HEAD   /{bucket}                → bucket exists check (200/404)
GET    /{bucket}?location       → GetBucketLocation (S3Conventions.Region, us-east-1)
GET    /{bucket}?uploads        → ListMultipartUploads XML
GET    /{bucket}                → ListObjects (marker, NextMarker only with a delimiter) or ?list-type=2 ListObjectsV2 (continuation-token = base64url of the last key or prefix, start-after, fetch-owner);
                                  max-keys default and cap 1000, negative or non-numeric → 400; encoding-type=url encodes per path segment, but V1 leaves the top-level Prefix raw because botocore does not decode it there
GET    /{bucket}?versions       → ListObjectVersions with key-marker paging; each object is its own "null" version (buckets are never versioned)
GET    /{bucket}?versioning     → empty VersioningConfiguration (never versioned)
GET|PUT|DELETE /{bucket}?acl|policy|cors|lifecycle|tagging|… → 501 NotImplemented (S3Subresources, ObjeX.Api/S3/); never falls through to create or delete
PUT    /{bucket}                → create bucket; repeating it for your own bucket is a 200 no-op (us-east-1 behaviour) unless it carries x-amz-acl, someone else's → 409 BucketAlreadyExists
DELETE /{bucket}                → delete bucket
PUT    /{bucket}/{*key}         → upload object (returns ETag header); x-amz-copy-source → CopyObject (onto itself only with x-amz-metadata-directive: REPLACE, which takes Content-Type and x-amz-meta-* from the request); x-amz-meta-* captured
GET|PUT|DELETE /{bucket}/{*key}?acl|tagging|attributes|retention|… → 501 NotImplemented; never touches the object
PUT    /{bucket}/{*key}?partNumber=N&uploadId=X → UploadPart; upserts part, returns ETag header; with x-amz-copy-source it is UploadPartCopy (optional x-amz-copy-source-range bytes=first-last, CopyPartResult XML)
GET    /{bucket}/{*key}         → download object; ?download=true forces application/octet-stream attachment; Range requests supported; x-amz-meta-* and stored system headers returned; response-content-type/-cache-control/-content-disposition/-content-encoding/-content-language/-expires override them; x-objex-verify-integrity header triggers ETag re-hash (500 on mismatch; multipart objects are served without the check)
GET    /{bucket}/{*key}?uploadId=X → ListParts XML
HEAD   /{bucket}/{*key}         → object metadata (ETag, Content-Length, Content-Type, x-amz-meta-* and stored system headers)
DELETE /{bucket}/{*key}         → delete object (204)
DELETE /{bucket}/{*key}?uploadId=X → AbortMultipartUpload; deletes parts + session
POST   /{bucket}/{*key}?uploads → InitiateMultipartUpload; returns UploadId XML
POST   /{bucket}/{*key}?uploadId=X → CompleteMultipartUpload; assembles parts, saves object, returns ETag
POST   /{bucket}                → S3 POST Object (presigned POST); multipart form with policy + file
POST   /{bucket}?delete         → DeleteObjects (batch delete); XML body with key list
POST   /                        → S3 POST Object (bucketEndpoint mode); bucket from form field

# S3 implementation details:
# - SigV4Parser (ObjeX.Api/S3/SigV4Parser.cs) — parses Authorization header + presigned query params
# - SigV4Signer (ObjeX.Api/S3/SigV4Signer.cs) — canonical request, string-to-sign, HMAC key derivation
# - SigV4AuthMiddleware (ObjeX.Api/Middleware/) — orchestrates auth: lookup → timestamp → sig → payload hash
# - S3Xml helper (ObjeX.Api/S3/S3Xml.cs) — XML response builders, SecurityElement.Escape() for injection prevention
# - S3Errors constants (ObjeX.Api/S3/S3Errors.cs) — S3 error code strings
# - S3PostObjectEndpoint (ObjeX.Api/Endpoints/S3Endpoints/) — browser-based uploads via presigned POST policy
#   Auth is form-field-based (policy + X-Amz-Signature), not header SigV4. Middleware handles this as a third auth path.
#   The policy must carry a parsable expiration, otherwise 403 — it is the only time limit on a leaked signature. Malformed policy JSON is a 403 too, never a 500. Every form field needs a policy condition (403 otherwise), except policy, the X-Amz-* signing fields and x-ignore-*.
# - S3RequestBody (ObjeX.Api/S3/) — unwraps aws-chunked bodies for PUT and UploadPart. A body counts as aws-chunked only with x-amz-content-sha256: STREAMING-* or with Content-Encoding: aws-chunked plus x-amz-decoded-content-length; Content-Encoding: aws-chunked alone is just stored metadata. SDKs send that framing whenever they stream with a trailing checksum, the CLI does so over HTTPS. AwsChunkedStream copies chunk data straight into the caller's buffer; only header lines use a fixed 1 KB scratch buffer, so a declared chunk size never sizes an allocation. A chunk size that is not hex, wider than 8 hex digits or above 1 GiB, and a body that ends before its terminating chunk, throw InvalidDataException, which the S3 exception handler answers with 400 IncompleteBody. Chunk signatures and trailer checksums are not verified.
# - ObjectDeletion (ObjeX.Api/S3/) — shared row-then-blob delete for the S3 endpoints; DeleteManyAsync backs DeleteObjects: all rows in one DeleteObjectsAsync call, then one blob delete per key, so a metadata failure yields an <Error> for every key of the batch
# - 412 and 416 from Results.File get an S3 error document (PreconditionFailed, InvalidRange) from a small middleware in S3Pipeline
# - Every S3 response carries x-amz-request-id (HttpContext.TraceIdentifier); error documents repeat it as <RequestId>
# - ObjectHeaders (ObjeX.Api/S3/) — x-amz-meta-* plus Cache-Control, Content-Disposition, Content-Encoding (without aws-chunked), Content-Language and Expires,
#   stored together as JSON in BlobObject.CustomMetadata (and MultipartUpload.CustomMetadata from the initiate request); the UI's CustomMetadata.Parse shows only x-amz-meta-*
# - Preconditions (ObjeX.Api/S3/) — If-Match / If-None-Match on PUT and CompleteMultipartUpload (If-Match on a missing key → 404), x-amz-copy-source-if-* on CopyObject and UploadPartCopy; check-then-write, not atomic
# - A retried CompleteMultipartUpload succeeds when the object under the key has the ETag the listed parts produce
# - Kestrel writes response headers as Latin-1 (Program.cs), so non-ASCII x-amz-meta-* values round-trip for SDKs instead of failing the response
# - SigV4Signer canonicalizes the path from IHttpRequestFeature.RawTarget (decoded once per segment); tests pass the raw target via the X-ObjeX-Test-Raw-Target header because TestServer leaves it empty
# - ContentMd5 (ObjeX.Api/S3/) — verifies the optional Content-MD5 header on PUT object, UploadPart and DeleteObjects. Decoded before the body is read: 400 InvalidDigest unless base64 of 16 bytes. Compared after: 400 BadDigest, the blob or part file is deleted, no row is written. CopyObject, POST Object and CompleteMultipartUpload do not check it; for aws-chunked bodies the digest covers the decoded payload.
# - S3MultipartEndpoint (ObjeX.Api/Endpoints/S3Endpoints/) — Initiate + Complete (single MapPost dispatch on ?uploads vs ?uploadId)
# - Parts stored at {BasePath}/_multipart/{uploadId}/{partNumber}.part; cleaned up after Complete or Abort
# - Final ETag: MD5(binary concat of part MD5 bytes) + "-" + partCount (S3 multipart format)
# - CleanupAbandonedMultipartJob — weekly by default, deletes uploads older than SystemSettings.AbandonedMultipartDays (default 7)
# - UI single-file downloads use /api/objects/{bucket}/{*key}?download=true on port 9001 (cookie auth)
# - ZIP downloads use /api/objects/{bucket}/download?prefix= on port 9001
# - Presigned URL generation: GET /api/presign/{bucket}/{*key}?expires=N (port 9001, cookie auth)
#   → PresignedUrlGenerator (ObjeX.Core/Utilities/) — pure BCL, no ASP.NET dependency
#   → Expiry defaults/max: stored in SystemSettings DB row (Id=1); configurable via Settings UI — NOT via appsettings/env vars
#   → The max is enforced in SigV4AuthMiddleware on every presigned request (client-signed URLs included), capped at 604800 s like AWS; violations get 400 AuthorizationQueryParametersError
#   → UI: link icon button opens PresignedUrlDialog — chip presets + custom number/unit input, live expiry preview
```

---

## Key NuGet Packages

| Package | Used for |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite persistence |
| `EFCore.NamingConventions` | snake_case DB columns |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | User auth, password hashing, roles |
| `Serilog.AspNetCore` | Structured request logging |
| `Hangfire.Core` | Background job scheduling |
| `Hangfire.AspNetCore` | Hangfire DI + ASP.NET Core host integration |
| `Hangfire.Storage.SQLite` | Hangfire job store (reuses `objex.db`) |
| `Cronos` | Cron check and next-run preview for job schedules |
| `Radzen.Blazor` | UI component library |

---

## CI/CD

**`ci.yml`** — build + test gate, GitHub-hosted runner (`ubuntu-latest`). Triggers on push to `main` and all PRs. Runs twice, once per database (matrix `sqlite`, `postgresql`); the PostgreSQL leg sets `OBJEX_TEST_POSTGRES`, which makes `ObjeXFactory` create one PostgreSQL database per factory instead of a SQLite file. Locally: start any PostgreSQL and set `OBJEX_TEST_POSTGRES="Host=localhost;Port=5432;Username=…;Password=…"` (no `Database=`). Steps: checkout → setup .NET (from `global.json`) → restore → build Release → run xUnit test suite.

**S3 conformance** — `tests/s3-conformance/run.sh` runs ceph/s3-tests (pinned commit) against this checkout: it builds, starts its own ObjeX on ports 19000/19001 with throwaway data and a random seeded credential, runs `test_s3.py` without the markers in `excluded-markers.txt` and prints the in-scope pass rate (`summarize.py`; 83% on 2026-09-30). Manual for now; `MIN_PASS_RATE` makes it a CI gate later. Run it after changing S3 endpoints.

**`cd.yml`** — triggers only on a `v*` tag push, so `latest` is always the last release. Runs the tests, builds multi-arch image (amd64/arm64) via Buildx + QEMU and pushes to GitHub Container Registry (`ghcr.io/centrolabs/objex:latest` + `ghcr.io/centrolabs/objex:<tag>`), then packages `deploy/helm/objex` with chart version `X.Y.Z` and appVersion `vX.Y.Z` and pushes it to `oci://ghcr.io/centrolabs/charts`. The image tag in the chart defaults to its appVersion; the repo copy carries `0.0.0`/`latest` placeholders. The image carries BuildKit SBOM and provenance attestations, plus a Sigstore-signed provenance from `actions/attest-build-provenance`; verify with `gh attestation verify oci://ghcr.io/centrolabs/objex:<tag> --owner centrolabs`. The `scan` job builds an SPDX SBOM of the pushed image with Syft (`anchore/sbom-action`), scans it with Grype and uploads the SARIF to Code Scanning; the scan never fails the release, because the image is already public by then. The release job attaches `objex-<tag>.spdx.json`. Uses `GITHUB_TOKEN` (automatic, no manual secrets needed).

**Release** — tag a commit on `main` as `vX.Y.Z` and push the tag; no release commit. Optional hand-written notes go in `.github/release-notes/vX.Y.Z.md`; CD puts them above the generated release notes. CD strips the `v` and passes the rest as the `VERSION` build arg (`-p:Version`), builds and pushes the image and creates the GitHub release. `Directory.Build.props` keeps `<Version>0.0.0</Version>`, so the lock files never change on a release. The nav footer shows `ObjeX <version> (<sha>)`, and `ObjeX dev (<sha>)` for a local build, whose version is 0.0.0 (`AppVersion.Display`); the sha comes from the SDK locally and from the `SOURCE_REVISION` build arg in Docker. The same arg sets the image labels `org.opencontainers.image.revision` and `org.opencontainers.image.source`.

**`.github/dependabot.yml`** — weekly Monday PRs: one `nuget` group for all minor and patch updates (major updates come as single PRs, max 5 open) and one `github-actions` group.

**`.dockerignore`** is present at repo root. It excludes `src/**/bin/`, `src/**/obj/`, `data/`, `.git/`, IDE folders, and local config overrides (`appsettings.Development.json`). Without it, `docker build` would send ~230MB of build artifacts as context on every build.

---

## Run Locally

```bash
cd src/ObjeX.Api
dotnet run
# → http://localhost:9001  (login: admin / admin, forced password change on first login)
# → http://localhost:9001/jobs       (background jobs, Admin)
# → http://localhost:9001/jobs       (Jobs page; the Hangfire dashboard is at /hangfire)
# → http://localhost:9001/health
```

## Database Provider

ObjeX supports SQLite (default) and PostgreSQL. Set via `Database:Provider` config or `DATABASE_PROVIDER` env var.

| Provider | Value | Connection string example |
|---|---|---|
| SQLite (default) | `sqlite` | `Data Source=./data/db/objex.db` |
| PostgreSQL | `postgresql` | `Host=localhost;Database=objex;Username=objex;Password=secret` |

Invalid provider or mismatched connection string → fail-fast at startup with clear error.

Hangfire storage follows the same switch: `Hangfire.Storage.SQLite` for SQLite, `Hangfire.PostgreSql` for PostgreSQL. SQLite PRAGMAs (WAL, busy_timeout) only run on SQLite.

**Docker Compose with Postgres:** `docker compose -f deploy/docker-compose.postgres.yml up`

## EF Migrations

**Dual-provider setup:** SQLite migrations live in `ObjeX.Infrastructure/Migrations/`. PostgreSQL migrations live in `ObjeX.Migrations.PostgreSql/Migrations/`. Each assembly is independent.

```bash
cd src/ObjeX.Api

# SQLite migration (default)
dotnet ef migrations add <Name> --project ../ObjeX.Infrastructure

# PostgreSQL migration (requires a running Postgres instance)
Database__Provider=postgresql \
  ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=objex;Username=objex;Password=objex" \
  dotnet ef migrations add <Name> --project ../ObjeX.Migrations.PostgreSql

# Apply — or just run the app (auto-migrates on startup)
dotnet ef database update
```

When adding a new model or changing an existing one, generate a migration for **both** providers.
