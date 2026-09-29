# API

## S3 API — port `9000`

Auth is AWS Signature Version 4. Create credentials in the web UI under **Settings → S3 Credentials**.

| Method | Path | Description |
|---|---|---|
| `GET` | `/` | List buckets |
| `HEAD` | `/{bucket}` | Bucket exists |
| `GET` | `/{bucket}?location` | Bucket location (`us-east-1`) |
| `GET` | `/{bucket}?uploads` | List multipart uploads |
| `PUT` | `/{bucket}` | Create bucket |
| `DELETE` | `/{bucket}` | Delete bucket |
| `PUT` | `/{bucket}/{key}` | Upload object; `x-amz-copy-source` copies (onto itself only with `x-amz-metadata-directive: REPLACE`), `x-amz-meta-*` is stored |
| `PUT` | `/{bucket}/{key}?partNumber=N&uploadId=X` | Upload part |
| `GET` | `/{bucket}/{key}` | Download object; range requests; `?download=true` forces attachment |
| `GET` | `/{bucket}/{key}?uploadId=X` | List parts |
| `HEAD` | `/{bucket}/{key}` | Object metadata |
| `DELETE` | `/{bucket}/{key}` | Delete object |
| `DELETE` | `/{bucket}/{key}?uploadId=X` | Abort multipart upload |
| `POST` | `/{bucket}/{key}?uploads` | Start multipart upload |
| `POST` | `/{bucket}/{key}?uploadId=X` | Complete multipart upload |
| `POST` | `/{bucket}?delete` | Delete several objects |
| `POST` | `/{bucket}`, `/` | Presigned POST upload (form fields) |
| `GET` | `/{bucket}?versions` | List versions; each object is its own `null` version |
| `GET` | `/{bucket}?versioning` | Empty configuration: buckets are never versioned |
| any | `?acl`, `?policy`, `?cors`, `?lifecycle`, `?tagging` and other subresources, on buckets and objects | `501 Not Implemented` |

Send `x-objex-verify-integrity: true` on a download to re-hash the object before streaming it.

## Web endpoints — port `9001`

Used by the web UI; they need the login cookie unless noted.

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/objects/{bucket}/{key}` | Download; images, audio, video, PDF and plain text open inline |
| `GET` | `/api/objects/{bucket}/download` | ZIP download; `?prefix=` scopes to a folder |
| `GET` | `/api/presign/{bucket}/{key}` | Presigned URL; `?expires=N` in seconds |
| `POST` | `/account/login` | Form login, no cookie needed |
| `GET` | `/account/logout` | Log out |
| `GET` | `/health`, `/health/live` | Liveness, no cookie needed |
| `GET` | `/health/ready` | Database and blob storage check, no cookie needed |
| `GET` | `/metrics` | Prometheus, with `Metrics:Enabled=true`; no cookie, Bearer token when `Metrics:Token` is set |
| `GET` | `/hangfire` | Job dashboard, Admin only |
