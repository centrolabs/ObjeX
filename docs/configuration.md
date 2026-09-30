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

Log files go to `./data/logs/objex-YYYYMMDD.log`: daily, 30 days retention, compact JSON.

Presigned URL expiry (default 1 hour, max 7 days) and storage quotas are set in the web UI under **Settings**, not in configuration.

## Example

```json
{
  "ConnectionStrings": { "DefaultConnection": "Data Source=/opt/objex/data/db/objex.db" },
  "Storage": { "BasePath": "/opt/objex/data/blobs" },
  "DefaultAdmin": { "Username": "myadmin", "Email": "admin@example.com", "Password": "changeme" }
}
```

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
