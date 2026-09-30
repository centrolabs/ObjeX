# API

## S3 API — port `9000`

Auth is AWS Signature Version 4. Create credentials in the web UI under **Settings → S3 Credentials**.

| Method | Path | Description |
|---|---|---|
| `GET` | `/` | List buckets; `prefix`, `max-buckets`, `continuation-token` |
| `GET` | `/{bucket}` | List objects; `prefix`, `delimiter`, `max-keys` (≤ 1000), `marker`, `encoding-type=url` |
| `GET` | `/{bucket}?list-type=2` | List objects V2; `continuation-token`, `start-after`, `fetch-owner` |
| `HEAD` | `/{bucket}` | Bucket exists |
| `GET` | `/{bucket}?location` | Bucket location (`us-east-1`) |
| `GET` | `/{bucket}?uploads` | List multipart uploads |
| `PUT` | `/{bucket}` | Create bucket; repeating it for your own bucket is a no-op |
| `DELETE` | `/{bucket}` | Delete bucket |
| `PUT` | `/{bucket}/{key}` | Upload object; `x-amz-copy-source` copies (onto itself only with `x-amz-metadata-directive: REPLACE`); `x-amz-meta-*`, `Cache-Control`, `Content-Disposition`, `Content-Encoding`, `Content-Language` and `Expires` are stored; `If-Match` / `If-None-Match: *` make it conditional |
| `PUT` | `/{bucket}/{key}?partNumber=N&uploadId=X` | Upload part; with `x-amz-copy-source` (and `x-amz-copy-source-range`) copy it from an object |
| `GET` | `/{bucket}/{key}` | Download object; range requests; `?download=true` forces attachment; `response-content-type` and the other `response-*` parameters override the stored headers |
| `GET` | `/{bucket}/{key}?uploadId=X` | List parts |
| `HEAD` | `/{bucket}/{key}` | Object metadata |
| `DELETE` | `/{bucket}/{key}` | Delete object |
| `DELETE` | `/{bucket}/{key}?uploadId=X` | Abort multipart upload |
| `POST` | `/{bucket}/{key}?uploads` | Start multipart upload |
| `POST` | `/{bucket}/{key}?uploadId=X` | Complete multipart upload |
| `POST` | `/{bucket}?delete` | Delete several objects |
| `POST` | `/{bucket}`, `/` | Presigned POST upload (form fields) |
| `GET` | `/{bucket}?versions` | List versions with `key-marker`; each object is its own `null` version |
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
| `GET` | `/hangfire` | Hangfire job dashboard, Admin only, Development only (the UI has its own Jobs page) |
