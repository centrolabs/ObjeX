# ObjeX - Self-Hosted Blob Storage

**Goal**: Self-hostable, open-source blob storage with S3-compatible API
**Stack**: .NET 10 API + Blazor Server UI + SQLite + Filesystem storage
**Status**: Active development — core API, auth, and UI implemented

> **Scope:** Single-node object storage for homelabs, internal tools, and dev/test environments. Not yet suitable for mission-critical data — no replication, high availability, or point-in-time recovery.

---

## Quick Start

```bash
git clone https://github.com/youruser/ObjeX.git
cd ObjeX/src/ObjeX.Api
dotnet run
```

Open **http://localhost:8080** — log in with `admin` / `admin`.

- **Blazor UI**: http://localhost:8080
- **API docs (Scalar)**: http://localhost:8080/scalar/v1
- **Job dashboard**: http://localhost:8080/hangfire
- **Health check**: http://localhost:8080/health (liveness)
- **Health check (readiness)**: http://localhost:8080/health/ready

> ⚠️ Change the default admin credentials before exposing the instance publicly. Set `DefaultAdmin:Username`, `DefaultAdmin:Email`, and `DefaultAdmin:Password` in `appsettings.json` or environment variables.

---

## API Authentication

All API endpoints require authentication via **cookie session** (browser) or **API key** (external clients).

### API Key

Create a key in the Settings page (`/settings`) or via the API:

```bash
# 1. Log in to get a session cookie
curl -c cookies.txt -X POST http://localhost:8080/account/login \
  -d "login=admin&password=admin"

# 2. Create an API key
curl -b cookies.txt -X POST http://localhost:8080/api/keys \
  -H "Content-Type: application/json" \
  -d '{"name":"my-key","expiresInDays":365}'
# → {"key":"obx_...","name":"my-key","expiresAt":"..."}

# 3. Use the key for all subsequent requests
export OBX_KEY="obx_..."
```

### Using an API Key

```bash
# Create a bucket
curl -X POST "http://localhost:8080/api/buckets?name=my-bucket" \
  -H "X-API-Key: $OBX_KEY"

# Upload an object
curl -X PUT http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY" \
  --data-binary "Hello, ObjeX!"

# Download it
curl http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY"

# List objects (flat)
curl http://localhost:8080/api/objects/my-bucket/ \
  -H "X-API-Key: $OBX_KEY"

# List objects with virtual folder navigation
curl "http://localhost:8080/api/objects/my-bucket/?prefix=images/&delimiter=/" \
  -H "X-API-Key: $OBX_KEY"

# Download a folder as ZIP
curl "http://localhost:8080/api/objects/my-bucket/download?prefix=images/" \
  -H "X-API-Key: $OBX_KEY" -o images.zip

# Delete an object
curl -X DELETE http://localhost:8080/api/objects/my-bucket/hello.txt \
  -H "X-API-Key: $OBX_KEY"

# Delete a bucket
curl -X DELETE http://localhost:8080/api/buckets/my-bucket \
  -H "X-API-Key: $OBX_KEY"
```

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│              ASP.NET Core 10 App                │
│                                                 │
│  ├─ Blazor Server UI (/)                        │
│  ├─ REST API (/api/*)                           │
│  ├─ Auth endpoints (/account/login, /logout)    │
│  └─ Scalar API Docs (/scalar/v1)               │
│                                                 │
│  Auth pipeline:                                 │
│  Cookie ──┐                                     │
│           ├─→ UseAuthorization → endpoints      │
│  API Key ─┘                                     │
│                                                 │
│  ┌─────────────────────────────────────────┐    │
│  │  Storage                                │    │
│  │  ./data/blobs/  (content-addressed FS)  │    │
│  │  ./objex.db     (SQLite — metadata +    │    │
│  │                  identity + job store)  │    │
│  └─────────────────────────────────────────┘    │
└─────────────────────────────────────────────────┘
```

### Project Structure

```
ObjeX/
├── src/
│   ├── ObjeX.Api/              # ASP.NET Core host
│   │   ├── Auth/               # HangfireAuthorizationFilter, NoOpEmailSender
│   │   ├── Endpoints/          # BucketEndpoints, ObjectEndpoints, ApiKeyEndpoints
│   │   ├── Middleware/         # ApiKeyAuthenticationMiddleware
│   │   └── Program.cs          # DI, middleware pipeline, EF migrations, admin seed
│   │
│   ├── ObjeX.Web/              # Blazor Server UI
│   │   └── Components/
│   │       ├── Pages/          # Dashboard, Buckets, Objects, Settings, Login
│   │       ├── Dialogs/        # Create/upload/API key dialogs
│   │       └── Layout/         # MainLayout (auth gate), NavMenu, EmptyLayout
│   │
│   ├── ObjeX.Core/             # Domain — no framework dependencies
│   │   ├── Interfaces/         # IMetadataService, IObjectStorageService, IHashService
│   │   ├── Models/             # Bucket, BlobObject, ApiKey, User
│   │   └── Validation/         # BucketNameValidator
│   │
│   └── ObjeX.Infrastructure/   # Implementations
│       ├── Data/               # ObjeXDbContext (IdentityDbContext<User>)
│       ├── Hashing/            # Sha256HashService
│       ├── Jobs/               # CleanupOrphanedBlobsJob (Hangfire)
│       ├── Metadata/           # SqliteMetadataService
│       ├── Migrations/         # EF Core migrations
│       └── Storage/            # FileSystemStorageService
```

---

## API Endpoints

All endpoints except `/account/*` and `/health/*` require authentication (`X-API-Key` header or session cookie).

### Buckets — `/api/buckets`

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/buckets` | List all buckets |
| `POST` | `/api/buckets?name={name}` | Create a bucket |
| `GET` | `/api/buckets/{name}` | Get bucket details |
| `DELETE` | `/api/buckets/{name}` | Delete a bucket |

Bucket name rules: 3–63 chars, lowercase alphanumeric and hyphens, no consecutive hyphens, cannot start/end with hyphen.

### Objects — `/api/objects/{bucketName}`

| Method | Path | Description |
|--------|------|-------------|
| `PUT` | `/api/objects/{bucket}/{*key}` | Upload an object (streaming) |
| `GET` | `/api/objects/{bucket}/{*key}` | Download an object |
| `DELETE` | `/api/objects/{bucket}/{*key}` | Delete an object |
| `GET` | `/api/objects/{bucket}/` | List objects — accepts `?prefix=&delimiter=`; returns `{ objects, commonPrefixes }` |
| `GET` | `/api/objects/{bucket}/download` | Download objects as ZIP — accepts `?prefix=` to scope to a folder |

Object keys support slashes (virtual folders): `PUT /api/objects/my-bucket/images/photo.jpg`

Upload response:
```json
{ "key": "hello.txt", "etag": "a1b2c3...", "size": 13 }
```

### API Keys — `/api/keys`

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/keys` | Create a new API key |
| `GET` | `/api/keys` | List your API keys (no key value) |
| `DELETE` | `/api/keys/{id}` | Delete an API key |

Create request: `{"name":"my-key","expiresInDays":365}` (omit for 10-year default)
Create response: `{"key":"obx_...","name":"...","expiresAt":"..."}` — **key value shown once only**

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core 10 |
| API | Minimal APIs |
| UI | Blazor Server (Interactive SSR) + Radzen Blazor |
| API Docs | Scalar + OpenAPI |
| Auth | ASP.NET Core Identity (cookies) + custom API key middleware |
| Database | SQLite via EF Core 10 (snake_case cols, auto-migrated) |
| Blob store | Filesystem, content-addressable SHA256 paths |
| Background jobs | Hangfire (SQLite-backed, dashboard at `/hangfire`) |
| Logging | Serilog (console + request logging) |
| Compression | Response compression (HTTPS-enabled) |

---

## Configuration

No config required for local dev. Defaults (from `appsettings.json`):

| Setting | Default |
|---------|---------|
| Port | `http://localhost:8080` |
| Database | `./data/db/objex.db` (relative to working directory) |
| Blob storage | `./data/blobs` (relative to working directory) |
| Admin username | `admin` |
| Admin email | `admin@objex.local` |
| Admin password | `admin` |

Override via `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/opt/objex/data/db/objex.db"
  },
  "Storage": {
    "BasePath": "/opt/objex/data/blobs"
  },
  "DefaultAdmin": {
    "Username": "myadmin",
    "Email": "admin@example.com",
    "Password": "changeme"
  }
}
```

> ⚠️ Change default admin credentials before exposing the instance publicly.

### SQLite Limitations

SQLite is the right choice for single-node homelab use — zero config, no separate process, trivially backed up with `cp`. It becomes a bottleneck in specific scenarios:

**What works fine:**
- Personal or small-team use (handful of concurrent users)
- Bursty uploads — SQLite handles short write spikes well in WAL mode
- Read-heavy workloads — WAL mode allows concurrent readers with no lock contention

**What to watch out for:**
- **Sustained concurrent writes** — Hangfire polls every few seconds while EF Core writes on every upload/delete/key rotation. Under heavy parallel upload bursts this can produce `SQLITE_BUSY` retries and degraded throughput
- **Network filesystems** — do not host `objex.db` on NFS, SMB, or any network-mounted path. SQLite uses POSIX advisory locks which are unreliable over NFS and can cause silent database corruption
- **Not benchmarked** — no formal throughput testing has been done. If you need numbers, run your own load test against your hardware

**Multi-instance:** startup migration (`db.Database.Migrate()`) is not safe for concurrent multi-instance deployments — if two processes start simultaneously, both race on schema migration. SQLite's file lock serializes this in practice but it's not a guarantee. ObjeX is single-node by design; if you ever run multiple instances, extract migrations into a dedicated pre-start step.

**Architecture note:** Hangfire, EF Core (metadata + Identity), and the app all share one `objex.db` file. Separating Hangfire onto its own SQLite file or an in-memory store is a future improvement. For now, the weekly cleanup job is the only significant Hangfire write activity.

**Upgrade path:** The `IMetadataService` interface is the only thing that needs a new implementation to swap SQLite for PostgreSQL. See roadmap.

### Backup & Restore

> **Current state:** no built-in backup tooling. Manual procedure only.

#### What needs to be backed up

ObjeX data lives in two places that must be backed up **together and consistently**:

```
data/
├── db/objex.db     # SQLite — all metadata, user accounts, API keys, bucket definitions
└── blobs/          # content-addressed blob files (SHA256-named .blob files)
```

The logical key → physical blob mapping exists **only in the database**. If you lose `objex.db` but keep the blobs, you have a directory of `a3f7c2....blob` files with no way to know which object each one represents. There is currently no tool to rebuild the index from disk.

#### Docker (recommended setup)

Data lives in a named Docker volume. To back it up:

```bash
# Stop the container first (ensures DB is not mid-write)
docker compose stop objex

# Copy the volume to a backup location
docker run --rm \
  -v objex_data:/data \
  -v /your/backup/path:/backup \
  alpine cp -a /data /backup/objex-$(date +%Y%m%d)

# Restart
docker compose start objex
```

Hot backup (without stopping) is possible using SQLite's online backup:
```bash
docker exec objex sqlite3 /data/db/objex.db ".backup /data/db/objex.db.bak"
```
Then copy the `.bak` file and the blobs. There is a small race window between the DB backup and the blob copy — any blobs written in that window will be orphaned and cleaned up by the weekly Hangfire GC job. No data loss, but a slightly inconsistent snapshot is possible.

#### Bare metal / direct deploy

```bash
# Stop the app first
pkill -f ObjeX.Api.dll

cp -a ~/objex-live/data/ ~/backups/objex-$(date +%Y%m%d)/

# Restart
screen -dmS objex dotnet ~/objex-live/ObjeX.Api.dll --urls "http://0.0.0.0:8080"
```

#### Restore

1. Stop the running instance
2. Replace `data/` with the backup copy
3. Start the instance — EF Core will validate the schema on startup
4. Hit `/health/ready` to confirm DB connectivity and blob storage are both healthy
5. Spot-check a few object downloads to verify blob integrity

#### Consistency guarantees

| Scenario | Outcome |
|----------|---------|
| DB newer than blobs | Object records exist with no backing blob → download returns 404 |
| Blobs newer than DB | Orphaned blobs → cleaned up automatically by weekly Hangfire GC |
| Both from same stopped snapshot | Fully consistent |

### Encryption

ObjeX does not encrypt blobs or metadata at the application level. For data at rest, rely on full-disk encryption at the host (e.g. LUKS, BitLocker, or encrypted cloud volumes). For data in transit, run ObjeX behind a TLS-terminating reverse proxy (nginx, Caddy, Traefik).

### HTTP Security Headers

ObjeX sets the following headers on every response:

| Header | Value |
|--------|-------|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `X-Permitted-Cross-Domain-Policies` | `none` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Strict-Transport-Security` | `max-age=63072000; includeSubDomains` (non-dev only) |

Content Security Policy (CSP) is not yet set — Blazor Server requires inline scripts and a `ws://` WebSocket connection for SignalR, making a safe policy non-trivial. Deferred to a future hardening pass.

### Blob Layout on Disk

Blobs use **content-addressable hashed paths** — the physical filename is a SHA256 hash of `"{bucketName}/{key}"`, spread across a 2-level directory tree:

```
/data/
├── blobs/
│   └── {bucket}/
│       └── {L1}/           # first 2 chars of SHA256 hash
│           └── {L2}/       # next 2 chars of SHA256 hash
│               └── {hash}.blob
└── objex.db                # SQLite — metadata + identity + Hangfire jobs
```

The logical key (e.g. `images/2024/photo.jpg`) lives in the database only.

---

## What's Implemented

- [x] Clean Architecture (Core / Infrastructure / API / Web)
- [x] Bucket CRUD with name validation
- [x] Object upload (streaming), download, delete, list
- [x] Blazor bucket detail page (`/buckets/{name}`) — virtual folder navigation, breadcrumb, New Folder, upload (uploads into current folder), per-object/folder download + delete
- [x] ZIP download of any folder via API
- [x] Dark mode with system preference detection (cookie-persisted, toggle in Settings)
- [x] Atomic blob writes — write to `.tmp`, then `File.Move` into final path; stale `.tmp` cleanup on startup
- [x] ETag computation (MD5) on upload
- [x] SQLite metadata store via EF Core (auto-migrated on startup)
- [x] Content-addressable filesystem blob store (SHA256 hashed paths, 2-level nesting)
- [x] Orphaned blob GC via Hangfire background job (weekly, results in dashboard)
- [x] ASP.NET Core Identity — User model, password hashing, roles (Admin/User)
- [x] Default admin seeded on first run
- [x] Login/logout UI (Blazor + Radzen, username or email, toast on error)
- [x] Global Blazor route protection (all pages require login)
- [x] Cookie auth for browser sessions
- [x] API key auth for external clients (`X-API-Key` header, `obx_` prefix keys)
- [x] API key management UI (Settings page — create, list, delete)
- [x] Proper 401 responses for unauthenticated API requests (not 302 redirects)
- [x] Hangfire dashboard at `/hangfire` (Admin role required)
- [x] Scalar interactive API docs at `/scalar/v1`
- [x] Health checks — `/health/live` (liveness) and `/health/ready` (readiness: DB + blob storage)
- [x] Serilog structured logging + request logging
- [x] Response compression

---

## Roadmap

See [ROADMAP.md](./ROADMAP.md) for the full plan.

- [x] **Content-addressable storage** — SHA256 hashed blob paths, orphaned blob GC
- [x] **Blazor UI** — dashboard (`/`), bucket list (`/buckets`), bucket detail + file browser (`/buckets/{name}`), drag-drop upload, per-object download/delete, API key management (`/settings`)
- [x] **Authentication** — Identity, cookie + API key dual auth, login/logout UI, admin seeding
- [x] **API Key system** — `X-API-Key` middleware, key management endpoints + UI
- [x] **Dockerize** — multi-stage Dockerfile, docker-compose, multi-arch (amd64/arm64)
- [x] **Virtual folder navigation** — prefix/delimiter listing, New Folder, ZIP download, folder delete
- [x] **Dark mode** — system preference detection, cookie persistence, toggle in Settings
- [ ] **S3 Compatibility** — AWS Sig V4, XML responses, aws-cli/boto3 support
- [ ] **Multipart Upload** — 5GB+ files, Initiate/UploadPart/Complete
- [ ] **Presigned URLs** — HMAC-SHA256 signed download + upload links
- [ ] **User Management UI** — registration, user list, password reset
- [ ] **Bucket Permissions** — per-bucket ACL, per-user read/write/delete
- [ ] **Teams & Organizations** — multi-tenant, quotas, team roles
- [ ] **Storage backends** — swap `FileSystemStorageService` for cloud storage
- [ ] **PostgreSQL support** — swap SQLite via `IMetadataService` interface

---

## CI/CD

Two GitHub Actions workflows in `.github/workflows/`:

| Workflow | File | Trigger | Runner |
|----------|------|---------|--------|
| CI | `ci.yml` | Push to `main`, any PR | `ubuntu-latest` (GitHub-hosted) |
| CD | `cd.yml` | Push to `main`, manual dispatch | Self-hosted (`objex`, `cd`, `dev` labels) |

**CI** — build-only gate: restore → build Release → fail fast on compile errors. No tests yet.

**CD (dev instance)** — deploys to `~/objex-live/` on the self-hosted runner VM:
1. Build + publish (Debug, `ASPNETCORE_ENVIRONMENT=Development`)
2. `pkill -f ObjeX.Api.dll` to stop the running instance
3. `rsync --exclude='data/'` to `~/objex-live/` — data directories are preserved across deploys
4. Start via `screen -dmS objex dotnet ObjeX.Api.dll --urls http://0.0.0.0:8080`

Data layout on the dev VM:
```
~/objex-live/
├── ObjeX.Api.dll       # published app
├── appsettings.json    # bundled config
└── data/
    ├── db/objex.db     # SQLite database (preserved by rsync --exclude)
    └── blobs/          # blob files (preserved by rsync --exclude)
```

## Testing

**Current state:** CI is build-only — no automated tests exist yet. The scenarios below are the known gaps before ObjeX can be considered production-ready.

### Hostile Scenario Coverage

| Scenario | Status | How it's handled |
|----------|--------|-----------------|
| Power loss mid-upload | ✅ Handled | Atomic write: `.tmp` → `File.Move`; stale `.tmp` cleaned on startup |
| Crash between blob write and metadata commit | ✅ Handled | Orphaned blob cleaned by weekly Hangfire GC |
| Path traversal in object key (`../../../etc/passwd`) | ✅ Handled | `SanitizeKey` strips `..` and normalises `\` → `/`; hashed paths never touch filesystem raw |
| Expired API key attempt | ✅ Handled | Middleware checks `ExpiresAt`, returns 401 |
| Missing blob file with valid metadata | ✅ Handled | `RetrieveAsync` throws `FileNotFoundException` → 404 |
| Disk full during upload | ⚠️ Partially handled | `.tmp` write fails and is cleaned up; API returns 500 — not tested under real disk pressure |
| Two concurrent uploads to same key | ⚠️ Untested | `File.Move(overwrite: true)` is atomic on Linux; DB upsert behavior under race not validated |
| DB locked under concurrent writes | ⚠️ Untested | EF Core retries on `SQLITE_BUSY`; no explicit retry policy or timeout tuning |
| Corrupt blob file with valid metadata | ❌ Not handled | Download returns corrupt bytes with 200 — no integrity check on read (ETag is stored but not verified) |
| Backup and restore drill | ❌ Not tested | Procedure documented; never actually drilled end-to-end |
| Large file upload (500MB+) | ⚠️ Untested | Blazor hub limit set to 500MB; streaming behavior under memory pressure unknown |
| Delete non-existent object | ✅ Handled | Idempotent — `File.Delete` is no-op if missing; DB delete is a no-op on missing row |
| Upload with no `Content-Type` header | ✅ Handled | Stored as `application/octet-stream` fallback |

### What needs automated tests

- Integration tests hitting a real SQLite DB (not mocked)
- Upload → download round-trip with ETag verification
- Concurrent upload stress test (same key, different keys)
- Auth boundary tests (no key, expired key, wrong key, valid cookie vs API key)
- Path traversal fuzzing on object keys
- Fault injection: disk full simulation, corrupted blob detection

---

## References

- [MinIO](https://github.com/minio/minio) — patterns reference
- [SeaweedFS](https://github.com/seaweedfs/seaweedfs) — simpler distributed architecture
- [Garage](https://garagehq.deuxfleurs.fr/) — Rust-based self-hosted object storage
- [AWS S3 API Reference](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html)
