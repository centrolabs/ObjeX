# Architecture diagrams

Source: [`diagrams/objex.mmd`](diagrams/objex.mmd), one Mermaid diagram per `%%% Title` section. Edit the source, then run [`diagrams/render.sh`](diagrams/render.sh) to regenerate the SVGs. For a live view with pan and zoom, serve the `diagrams` folder (`python3 -m http.server 8765`) and open [`diagrams/index.html`](diagrams/index.html).

The architecture diagram uses the ELK layout engine, which GitHub's Mermaid renderer does not ship. That is why the SVGs are committed, in a light and a dark variant; GitHub picks one via `<picture>`.

## 1. Architecture

One process, two Kestrel listeners. Requests are split by the TCP port they arrived on: the S3 pipeline on port 9000 authenticates with AWS Signature V4, the UI pipeline on port 9001 with the Identity cookie. Both end in the same services. Core interfaces are shown together with their Infrastructure implementation. Every node links to its source file.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/01-architecture.dark.svg">
  <img alt="Architecture" src="diagrams/01-architecture.svg">
</picture>

## 2. S3 PutObject · staged write

The order of checks on a single-part upload. The body is written to a temporary file first. Content-MD5 and the quota are checked after the write, and a failure disposes the staged file while the previous object keeps its bytes and its row. Only the commit moves the file into place, and only then is the row written together with the bucket statistics and the audit entry.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/02-s3-putobject-staged-write.dark.svg">
  <img alt="S3 PutObject · staged write" src="diagrams/02-s3-putobject-staged-write.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
sequenceDiagram
  autonumber
  participant C as S3 client
  participant A as SigV4AuthMiddleware
  participant E as S3ObjectEndpoint PUT
  participant Q as StorageQuota
  participant FS as FileSystemStorageService
  participant M as EfCoreMetadataService
  participant DB as Metadata DB
  participant D as Blob root

  C->>A: PUT /{bucket}/{key} · Authorization AWS4-HMAC-SHA256
  A->>DB: S3Credentials by AccessKeyId
  A->>A: timestamp ±15 min · signature · payload hash
  A-->>C: 403 on failure (S3 XML)
  A->>E: context.User = SigV4 identity
  E->>E: ObjectKeyValidator (400 InvalidArgument)
  E->>M: GetBucketAsync(bucket, owner filter) (404 NoSuchBucket)
  E->>FS: GetAvailableFreeSpace() (507 below Storage:MinimumFreeDiskBytes)
  E->>Q: CheckAsync with declared Content-Length
  Q->>DB: bucket owner · quota · used bytes (507 over quota)
  E->>E: ContentMd5.TryParse (400 InvalidDigest)
  E->>FS: StageAsync(HashingStream over decoded body)
  FS->>D: write {hash}.blob.{guid}.tmp
  Note over E,FS: ETag = MD5 from HashingStream
  E->>E: Content-MD5 mismatch → 400 BadDigest, staged blob disposed, old object untouched
  opt no Content-Length (chunked)
    E->>Q: CheckAsync with real size (507)
  end
  E->>FS: CommitAsync
  FS->>D: File.Move(tmp → {bucket}/{L1}/{L2}/{hash}.blob, overwrite)
  E->>M: SaveObjectAsync(BlobObject, auditUserId)
  M->>DB: upsert row · ObjectCount/TotalSize delta · AuditEntry (one transaction)
  E-->>C: 200 · ETag header
```

</details>

## 3. S3 Multipart upload

Initiate, the UploadPart loop with its upsert, and Complete with every validation the endpoint performs before it assembles the parts. The multipart ETag is the MD5 of the concatenated part MD5 bytes followed by the part count. Abort and the weekly cleanup job are noted at the bottom.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/03-s3-multipart-upload.dark.svg">
  <img alt="S3 Multipart upload" src="diagrams/03-s3-multipart-upload.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
sequenceDiagram
  autonumber
  participant C as S3 client
  participant MP as S3MultipartEndpoint
  participant OE as S3ObjectEndpoint PUT
  participant FS as FileSystemStorageService
  participant M as EfCoreMetadataService
  participant DB as Metadata DB
  participant D as Blob root

  C->>MP: POST /{bucket}/{key}?uploads
  MP->>M: GetBucketAsync (404 NoSuchBucket)
  MP->>DB: insert MultipartUpload (ContentType, InitiatedByUserId)
  MP-->>C: InitiateMultipartUploadResult · UploadId

  loop each part 1..10000
    C->>OE: PUT /{bucket}/{key}?partNumber=N&uploadId=X
    OE->>DB: MultipartUpload exists · caller is initiator or Admin/Manager (404 NoSuchUpload)
    OE->>FS: GetAvailableFreeSpace() (507)
    OE->>FS: StagePartAsync(uploadId, N, decoded body)
    FS->>D: _multipart/{uploadId}/{N}.part.tmp → commit
    OE->>OE: Content-MD5 vs part ETag (400 BadDigest)
    OE->>DB: upsert MultipartUploadPart (ETag, Size, StoragePath)
    OE-->>C: 200 · ETag of the part
  end

  C->>MP: POST /{bucket}/{key}?uploadId=X · XML part list
  MP->>DB: MultipartUpload + Parts (404 NoSuchUpload)
  MP->>MP: parts ascending (400 InvalidPartOrder) · ETag matches (400 InvalidPart) · ≥ 5 MB except last (400 EntityTooSmall)
  MP->>FS: GetAvailableFreeSpace() (507)
  MP->>DB: StorageQuota.CheckAsync(sum of part sizes) (507)
  MP->>FS: AssemblePartsAsync(bucket, key, ordered paths)
  FS->>D: concatenate parts into {hash}.blob
  Note over MP: ETag = MD5(concat of part MD5 bytes) + "-" + partCount
  MP->>M: SaveObjectAsync(BlobObject, auditUserId)
  M->>DB: row · stats delta · audit
  MP->>FS: DeletePartsAsync(uploadId)
  FS->>D: remove _multipart/{uploadId}
  MP->>DB: delete MultipartUpload (parts cascade)
  MP-->>C: CompleteMultipartUploadResult · ETag

  Note over C,D: DELETE ?uploadId=X aborts: part files and rows removed. CleanupAbandonedMultipartJob removes uploads older than 7 days every Sunday 05:00 UTC.
```

</details>

## 4. Browser login · cookie session

Blazor Server cannot set cookies from a circuit, so the login is a plain form POST to a minimal API endpoint. The diagram shows the lockout, deactivation and expired temporary password branches, the cookie lifetime with and without "Stay signed in", and the redirect to the forced password change.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/04-browser-login-cookie-session.dark.svg">
  <img alt="Browser login · cookie session" src="diagrams/04-browser-login-cookie-session.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
sequenceDiagram
  autonumber
  participant B as Browser
  participant L as Login.razor (static SSR)
  participant AE as POST /account/login
  participant SM as SignInManager
  participant DB as Metadata DB
  participant ML as MainLayout AuthorizeView

  B->>L: GET /login?returnUrl=…
  L->>L: read objex-remember cookie · PersistentComponentState
  L-->>B: plain HTML form (no Blazor handler, antiforgery disabled)
  B->>AE: POST login, password, returnUrl, rememberMe
  AE->>B: Set-Cookie objex-remember (1 year)
  AE->>SM: FindByEmailAsync if login contains @, else FindByNameAsync
  SM->>DB: AspNetUsers
  AE->>SM: CheckPasswordSignInAsync(lockoutOnFailure: true)
  alt locked out
    AE-->>B: 302 /login?error=1&msg=locked N minutes
  else deactivated
    AE-->>B: 302 /login?error=1&msg=deactivated
  else temporary password expired
    AE-->>B: 302 /login?error=1&msg=expired
  else wrong password or unknown user
    Note over AE,DB: failed count +1 · lock after Auth:Lockout:MaxFailedAttempts
    AE-->>B: 302 /login?error=1&login=…
  else success
    AE->>SM: SignInAsync(IsPersistent = rememberMe, ExpiresUtc = now + RememberMeDays)
    SM-->>B: Set-Cookie Identity.Application (session: 60 min sliding)
    alt MustChangePassword
      AE-->>B: 302 /change-password
    else
      AE-->>B: 302 returnUrl (local path only) or /
    end
  end
  B->>ML: GET page · cookie · SignalR circuit
  ML->>ML: AuthorizeView: Authorized → layout, NotAuthorized → RedirectToLogin (forceLoad)
  Note over B,ML: GET /account/logout clears the cookie and redirects to /login
```

</details>

## 5. Presigned URL · UI to S3 port

A presigned link is generated on the UI port with the cookie session and consumed on the S3 port with no headers at all. The middleware caps X-Amz-Expires at the maximum from SystemSettings, itself capped at the AWS limit of seven days.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/05-presigned-url-ui-to-s3-port.dark.svg">
  <img alt="Presigned URL · UI to S3 port" src="diagrams/05-presigned-url-ui-to-s3-port.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
sequenceDiagram
  autonumber
  participant U as Browser (PresignedUrlDialog)
  participant P as GET /api/presign · :9001
  participant DB as Metadata DB
  participant G as PresignedUrlGenerator (Core)
  participant R as Recipient
  participant A as SigV4AuthMiddleware · :9000
  participant E as S3ObjectEndpoint GET

  U->>P: /api/presign/{bucket}/{key}?expires=N&method=GET|PUT (cookie auth)
  P->>DB: bucket owned or Admin/Manager (403)
  P->>DB: first S3Credential of the user (400 if none)
  P->>DB: SystemSettings row 1 · default and max expiry
  P->>P: clamp expires to 1..max
  P->>G: Generate(S3:PublicUrl, bucket, key, AccessKeyId, SecretAccessKey, expires, method)
  G-->>P: URL with X-Amz-Algorithm, Credential, Date, Expires, SignedHeaders, Signature
  P-->>U: { url }
  U->>R: share the link
  R->>A: GET url on the S3 port · no headers needed
  A->>DB: S3Credentials by AccessKeyId
  A->>DB: SystemSettings max expiry
  A->>A: X-Amz-Expires in 1..min(max, 604800) else 400 AuthorizationQueryParametersError
  A->>A: now within X-Amz-Date + X-Amz-Expires else 403
  A->>A: signature over canonical query (UNSIGNED-PAYLOAD)
  A->>E: context.User = credential owner
  E-->>R: object stream · Range supported · x-amz-meta-* headers
```

</details>

## 6. Data model

All tables with keys, unique indexes and delete behaviour. Objects reference the bucket by name, not by its id. Audit entries carry the user id without a foreign key so they survive user deletion.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/06-data-model.dark.svg">
  <img alt="Data model" src="diagrams/06-data-model.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
erDiagram
  USER ||--o{ BUCKET : owns
  USER ||--o{ S3_CREDENTIAL : has
  BUCKET ||--o{ BLOB_OBJECT : "contains (FK bucket_name → name)"
  MULTIPART_UPLOAD ||--o{ MULTIPART_UPLOAD_PART : "parts (cascade)"
  USER ||--o{ AUDIT_ENTRY : "acted (user_id, no FK)"

  USER {
    string id PK "AspNetUsers, Identity"
    string user_name
    string email
    long storage_used_bytes
    long storage_quota_bytes "nullable, per-user override"
    bool is_deactivated
    bool must_change_password
    datetime temporary_password_expires_at "nullable"
    datetime lockout_end "Identity lockout"
  }
  BUCKET {
    guid id PK
    string name UK
    string owner_id FK "Restrict"
    int object_count
    long total_size
    datetime created_at
    datetime updated_at
  }
  BLOB_OBJECT {
    guid id PK
    string bucket_name FK "unique with key"
    string key
    long size
    string content_type
    string etag "MD5 hex, or MD5-N for multipart"
    string storage_path
    string custom_metadata "JSON of x-amz-meta-*"
    datetime created_at
    datetime updated_at
  }
  S3_CREDENTIAL {
    guid id PK
    string name
    string access_key_id UK "OBX + 17 chars"
    string secret_access_key "plain, needed for HMAC"
    string user_id FK "Cascade"
    datetime last_used_at "nullable"
  }
  MULTIPART_UPLOAD {
    guid id PK "UploadId"
    string bucket_name
    string key
    string content_type
    string initiated_by_user_id
    datetime created_at
  }
  MULTIPART_UPLOAD_PART {
    guid id PK
    guid upload_id FK "unique with part_number"
    int part_number
    string etag
    long size
    string storage_path
  }
  AUDIT_ENTRY {
    long id PK "autoincrement"
    string user_id "indexed"
    string action "indexed"
    string bucket_name "nullable, indexed"
    string key "nullable"
    string details "nullable"
    datetime timestamp "indexed"
  }
  SYSTEM_SETTINGS {
    int id PK "always 1"
    int presigned_url_default_expiry_seconds "3600"
    int presigned_url_max_expiry_seconds "604800"
    long default_storage_quota_bytes "nullable = unlimited"
  }
```

</details>

## 7. Staged blob lifecycle

The states a blob file can be in between the first byte and the metadata row, and which mechanism cleans up each failure mode: the disposal in the request, the startup sweep for stale temporary files, and the weekly orphan job.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="diagrams/07-staged-blob-lifecycle.dark.svg">
  <img alt="Staged blob lifecycle" src="diagrams/07-staged-blob-lifecycle.svg">
</picture>

<details><summary>Mermaid source</summary>

```mermaid
stateDiagram-v2
  direction LR
  [*] --> Staged : StageAsync writes {hash}.blob.{guid}.tmp
  Staged --> Committed : CommitAsync · File.Move overwrite
  Staged --> Discarded : DisposeAsync without commit<br/>(BadDigest, quota 507, client abort)
  Staged --> Orphaned : process crash
  Orphaned --> [*] : next startup deletes .tmp older than 1 h
  Discarded --> [*] : previous object keeps bytes and row
  Committed --> Recorded : SaveObjectAsync · row + stats delta + audit
  Recorded --> [*]
  Committed --> Orphaned : crash before SaveObjectAsync
  note right of Orphaned
    CleanupOrphanedBlobsJob (Sun 03:00 UTC) deletes .blob files
    with no row, unless modified within the last hour
  end note
```

</details>
