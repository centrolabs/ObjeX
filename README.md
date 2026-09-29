<img src="src/ObjeX.Api/wwwroot/favicon.svg" width="48" alt="ObjeX" />

# ObjeX

Self-hosted object storage with an S3-compatible API and a web UI. One container, SQLite or PostgreSQL, files on disk.

[![CI](https://github.com/centrolabs/ObjeX/actions/workflows/ci.yml/badge.svg)](https://github.com/centrolabs/ObjeX/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/centrolabs/ObjeX)](https://github.com/centrolabs/ObjeX/releases)
[![License](https://img.shields.io/github/license/centrolabs/ObjeX)](LICENSE)

<img width="800" alt="ObjeX web UI" src="https://github.com/user-attachments/assets/8cadcd71-de33-4554-a5a0-320362b35e68" />

## Features

- **S3 API** — AWS Signature V4, multipart upload, presigned GET and POST, copy, batch delete, range requests
- **Web UI** — folders, file previews, search, ZIP download, share links
- **Users** — Admin, Manager and User roles, storage quotas, audit log
- **Operations** — health checks, Prometheus metrics, weekly integrity and cleanup jobs
- **Deployment** — multi-arch Docker image (amd64, arm64), Docker Compose, Helm chart

Built for homelabs, internal tools and dev/test. ObjeX runs on a single node: no replication, no high availability.

## Quick start

```bash
docker run -d --name objex \
  -p 9001:9001 -p 9000:9000 \
  -v objex-data:/data \
  ghcr.io/centrolabs/objex:latest
```

1. Open http://localhost:9001 and log in with `admin` / `admin`. ObjeX asks you to change the password.
2. Create a bucket and an S3 credential under **Settings → S3 Credentials**.
3. Point any S3 client at `http://localhost:9000`:

```bash
aws configure set aws_access_key_id <access-key>
aws configure set aws_secret_access_key <secret-key>
aws --endpoint-url http://localhost:9000 s3 cp photo.jpg s3://my-bucket/
```

Other options: `docker compose -f deploy/docker-compose.yml up -d`, `docker compose -f deploy/docker-compose.postgres.yml up -d`, or `helm install objex oci://ghcr.io/centrolabs/charts/objex`.

## Ports

| Port | Serves | Auth |
|---|---|---|
| `9001` | Web UI, health, metrics | Login cookie |
| `9000` | S3 API | AWS Signature V4 |

Expose only the S3 port publicly and keep the UI on your internal network. Behind a reverse proxy, pass the `Host` header through unchanged, because S3 clients sign it.

## Configuration

ObjeX runs without configuration. Common settings as environment variables:

| Variable | Default | Purpose |
|---|---|---|
| `DefaultAdmin__Password` | `admin` | Password of the first admin |
| `S3__PublicUrl` | `http://localhost:9000` | Public S3 URL, used in presigned links |
| `Database__Provider` | `sqlite` | `sqlite` or `postgresql` |
| `ConnectionStrings__DefaultConnection` | `Data Source=/data/db/objex.db` | Database connection |
| `ReverseProxy__Enabled` | `false` | Trust `X-Forwarded-*` from known proxies |
| `Metrics__Enabled` | `false` | Expose `/metrics` for Prometheus |

All settings: [docs/configuration.md](docs/configuration.md).

## Documentation

- [Configuration](docs/configuration.md)
- [API](docs/api.md)
- [Architecture](docs/architecture.md)
- [Contributing](.github/CONTRIBUTING.md)
- [Security policy](.github/SECURITY.md)

## License

[MIT](LICENSE)
