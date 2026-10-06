# Configuration

ObjeX runs without configuration. Set values in `appsettings.json` or as environment variables; a `:` in a key becomes `__` in a variable name (`Storage:BasePath` → `Storage__BasePath`).

## Settings

| Setting | Default | Notes |
|---|---|---|
| `Server:UiPort` | `9001` | Web UI, health, metrics |
| `Server:S3Port` | `9000` | S3 API, AWS Signature V4 |
| `S3:PublicUrl` | `http://localhost:9000` | Public S3 URL, used in presigned links and the UI |
| `Database:Provider` | `sqlite` | `sqlite` or `postgresql` |
| `ConnectionStrings:DefaultConnection` | `Data Source=./data/db/objex.db` | `Data Source=/data/db/objex.db` in the container |
| `Database:AutoMigrate` | `true` | Run schema migrations on startup |
| `Storage:BasePath` | `./data/blobs` | `/data/blobs` in the container |
| `Storage:MaxUploadBytes` | unlimited | Cap per upload |
| `Storage:MinimumFreeDiskBytes` | `524288000` (500 MB) | Uploads get `507` below this free space |
| `Log:FilePath` | `./data/logs/objex-.log` | Daily log file, empty in the container |
| `Auth:Lockout:MaxFailedAttempts` | `5` | Failed logins before the account locks |
| `Auth:Lockout:DurationMinutes` | `5` | Lock duration |
| `Auth:RememberMeDays` | `30` | Cookie lifetime when "Stay signed in" is ticked |
| `ReverseProxy:Enabled` | `false` | See [Reverse proxy](#reverse-proxy) |
| `Metrics:Enabled` | `false` | Expose `/metrics` on the UI port |
| `Metrics:Token` | none | Require this Bearer token on `/metrics` |
| `DefaultAdmin:Username` | `admin` | First admin, created once |
| `DefaultAdmin:Email` | `admin@objex.local` | |
| `DefaultAdmin:Password` | `admin` | The built-in default forces a password change on first login |

Relative paths resolve against the content root: `src/ObjeX.Api/` under `dotnet run`, `/app` in the container.

Logs always go to stdout. Outside the container they also go to `./data/logs/objex-YYYYMMDD.log`: daily, 30 days retention, compact JSON. The container writes no log file; read its logs with `docker logs` or `kubectl logs`. An empty `Log:FilePath` turns the file off.

Presigned URL expiry (default 1 hour, max 7 days) and storage quotas are set in the web UI under **Settings**, not in configuration.

The background jobs are set on the **Jobs** page (Admin): enabled or disabled, daily or weekly with day and time, or a five-field cron, and their settings — the grace for new blob files of the orphan cleanup (default 60 minutes) and the age after which a multipart upload counts as abandoned (default 7 days). A schedule is stored with the browser's time zone, so a job set to 04:00 runs at 04:00 local time across daylight saving time. The defaults are weekly on Sunday, 03:00, 04:00, 05:00 and 06:00 UTC; the last one recounts bucket object counts and sizes that drifted from their objects. The server needs the time zone database (`tzdata`); the official image ships it.

## Example

```json
{
  "ConnectionStrings": { "DefaultConnection": "Data Source=/opt/objex/data/db/objex.db" },
  "Storage": { "BasePath": "/opt/objex/data/blobs" },
  "DefaultAdmin": { "Username": "myadmin", "Email": "admin@example.com", "Password": "changeme" }
}
```

## PostgreSQL search index

On PostgreSQL, object search uses a trigram index on the object keys. The migration creates the `pg_trgm` extension and the index. The database role needs the right to create extensions; from PostgreSQL 13 on, the database owner has it. Without it, ObjeX starts, search scans the table, and the log names the two statements to run as an administrator:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX IF NOT EXISTS ix_blob_objects_key_trgm ON blob_objects USING gin (lower(key) gin_trgm_ops);
```

The index takes about 100 MB per million objects. SQLite needs no index.

## Reverse proxy

ObjeX picks the API by the port a request arrives on, never by the `Host` header. Any hostname and any public port work in front of it. The proxy must:

1. Pass the `Host` header through unchanged. S3 clients sign it; a rewritten `Host` fails with `SignatureDoesNotMatch`.
2. Send `X-Forwarded-For` and `X-Forwarded-Proto`, and ObjeX must trust the proxy:

```json
{
  "ReverseProxy": {
    "Enabled": true,
    "KnownProxies": ["10.0.0.5"],
    "KnownNetworks": ["172.16.0.0/12"]
  }
}
```

As environment variables: `ReverseProxy__Enabled=true`, `ReverseProxy__KnownNetworks__0=172.16.0.0/12`. Loopback is always trusted once enabled.

Expose only the S3 port publicly. Set `S3:PublicUrl` to that public address, so server-side SDK calls and browser presigned URLs use the same host.

## Seeding

ObjeX can create buckets and one S3 credential on startup, owned by the default admin. Existing ones are skipped. See [deploy/docker-compose.yml](../deploy/docker-compose.yml).

| Variable | Purpose |
|---|---|
| `Seed__Buckets` | Comma-separated bucket names |
| `Seed__S3Credential__AccessKeyId` | Access key of your choice |
| `Seed__S3Credential__SecretAccessKey` | Secret of your choice |
| `Seed__S3Credential__Name` | Display name, default `seed-credential` |

## Roles

| Role | Access |
|---|---|
| Admin | Everything: users, roles, all buckets, Jobs page and Hangfire dashboard, settings |
| Manager | Users page, settings, all buckets; cannot change roles |
| User | Own buckets and S3 credentials |

The default admin cannot be deleted or demoted. Admin and Manager can unlock locked accounts on the **Users** page. Lockout is per account; there is no IP-based limiting, because behind CGNAT or a shared proxy one IP is many users.

## Blob layout

```
/data/
├── blobs/{bucket}/{L1}/{L2}/{sha256}.blob
└── db/objex.db
```

The file name is the SHA-256 of `{bucket}/{key}`; `L1` and `L2` are its first and second pair of hex characters. The key itself lives in the database only.

## Container user

The image runs as the user `app`, UID and GID 1654, never as root. A named volume takes its owner from the image. A bind mount needs that owner on the host first:

```bash
sudo chown -R 1654:1654 /srv/objex
```

Without it, ObjeX stops at startup with `Access to the path '/data/…' is denied`. The Helm chart sets `runAsUser`, `fsGroup` and a read-only root filesystem through `podSecurityContext` and `securityContext` in its values.
