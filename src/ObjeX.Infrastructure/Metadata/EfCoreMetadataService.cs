using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Metadata;

// Every call opens its own short context. A Blazor circuit keeps its scoped services for the whole session, and a context
// shared that long tracks every object it ever wrote: a later overwrite or delete then works on stale values or throws.
public class EfCoreMetadataService(IDbContextFactory<ObjeXDbContext> contexts) : IMetadataService
{
    public async Task<Bucket> CreateBucketAsync(Bucket bucket, string? auditUserId = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        var error = BucketNameValidator.GetValidationError(bucket.Name);
        if (error is not null)
            throw new ArgumentException(error, nameof(bucket));

        if (await ctx.Buckets.AnyAsync(b => b.Name == bucket.Name, ctk))
            throw new InvalidOperationException($"Bucket '{bucket.Name}' already exists.");

        ctx.Buckets.Add(bucket);
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "CreateBucket", BucketName = bucket.Name });
        await ctx.SaveChangesAsync(ctk);
        return bucket;
    }

    public async Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        var query = ctx.Buckets.AsNoTracking().Include(b => b.Owner).Where(b => b.Name == bucketName);
        if (ownerFilter is not null)
            query = query.Where(b => b.OwnerId == ownerFilter);
        return await query.FirstOrDefaultAsync(ctk);
    }

    public async Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        var query = ctx.Buckets.AsNoTracking().Include(b => b.Owner).AsQueryable();
        if (ownerFilter is not null)
            query = query.Where(b => b.OwnerId == ownerFilter);
        return await query.ToListAsync(ctk);
    }

    public async Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, string? auditUserId = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        await using var tx = await BeginBucketWriteAsync(ctx, bucketName, ctk);
        var bucket = await ctx.Buckets.FirstOrDefaultAsync(b => b.Name == bucketName, ctk);
        if (bucket is null) return;

        if (!isPrivileged && bucket.OwnerId != userId)
            throw new UnauthorizedAccessException($"You do not own bucket '{bucketName}'.");

        var objects = await ctx.BlobObjects.Where(o => o.BucketName == bucketName).ToListAsync(ctk);
        ctx.BlobObjects.RemoveRange(objects);
        ctx.Buckets.Remove(bucket);
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "DeleteBucket", BucketName = bucketName, Details = $"Objects deleted: {objects.Count}" });
        await ctx.SaveChangesAsync(ctk);
        await tx.CommitAsync(ctk);
    }

    public async Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        return await ctx.Buckets.AnyAsync(b => b.Name == bucketName, ctk);
    }

    public async Task<BlobObject> SaveObjectAsync(BlobObject blobObject, string? auditUserId = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        await using var tx = await BeginBucketWriteAsync(ctx, blobObject.BucketName, ctk);
        var existing = await ctx.BlobObjects.AsNoTracking()
            .FirstOrDefaultAsync(o => o.BucketName == blobObject.BucketName && o.Key == blobObject.Key, ctk);
        var sizeDelta = blobObject.Size - (existing?.Size ?? 0);
        var countDelta = existing is null ? 1 : 0;
        if (existing is not null)
        {
            existing.Size = blobObject.Size;
            existing.ETag = blobObject.ETag;
            existing.ContentType = blobObject.ContentType;
            existing.StoragePath = blobObject.StoragePath;
            existing.CustomMetadata = blobObject.CustomMetadata;
            existing.UpdatedAt = DateTime.UtcNow;
            ctx.BlobObjects.Update(existing);
        }
        else
        {
            ctx.BlobObjects.Add(blobObject);
        }
        if (auditUserId is not null)
            ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "PutObject", BucketName = blobObject.BucketName, Key = blobObject.Key, Details = $"Size: {FormatBytes(blobObject.Size)}, Type: {blobObject.ContentType}" });

        await ctx.SaveChangesAsync(ctk);
        await AdjustBucketStatsAsync(ctx, blobObject.BucketName, countDelta, sizeDelta, ctk);
        await tx.CommitAsync(ctk);

        return blobObject;
    }

    public async Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        return await ctx.BlobObjects.AsNoTracking()
            .FirstOrDefaultAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
    }

    public async Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null,
        string? startAfter = null, int? maxKeys = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        const int batchSize = 1000;
        var limit = maxKeys ?? int.MaxValue;
        var objects = new List<BlobObject>();
        var commonPrefixes = new List<string>();
        if (limit == 0)
            return new ListObjectsResult(objects, commonPrefixes);

        var bound = startAfter;
        var inclusive = false;
        string? skip = null;
        string? last = null;

        while (true)
        {
            var query = ctx.BlobObjects.AsNoTracking().Where(o => o.BucketName == bucketName);
            if (!string.IsNullOrEmpty(prefix))
                query = query.Where(o => o.Key.StartsWith(prefix));
            if (!string.IsNullOrEmpty(bound))
                query = KeysFrom(ctx, query, bound, inclusive);
            var page = await OrderByKey(ctx, query).Take(batchSize).ToListAsync(ctk);

            // Ordinal compares UTF-16 code units, which differs from UTF-8 byte order only for supplementary characters.
            foreach (var obj in page)
            {
                if (skip is not null && obj.Key.StartsWith(skip, StringComparison.Ordinal))
                    continue;

                var commonPrefix = CommonPrefixOf(obj.Key, prefix, delimiter);
                if (commonPrefix is not null && startAfter is not null && string.CompareOrdinal(commonPrefix, startAfter) <= 0)
                {
                    skip = commonPrefix;
                    continue;
                }

                if (objects.Count + commonPrefixes.Count == limit)
                    return new ListObjectsResult(objects, commonPrefixes, IsTruncated: true, NextMarker: last);

                if (commonPrefix is null)
                {
                    objects.Add(obj);
                    last = obj.Key;
                }
                else
                {
                    commonPrefixes.Add(commonPrefix);
                    last = skip = commonPrefix;
                }
            }

            if (page.Count < batchSize)
                return new ListObjectsResult(objects, commonPrefixes);

            // Jump past every key under the prefix the page ended in instead of reading them all.
            (bound, inclusive) = skip is not null && page[^1].Key.StartsWith(skip, StringComparison.Ordinal) && Successor(skip) is { } next
                ? (next, true)
                : (page[^1].Key, false);
        }
    }

    private static string? CommonPrefixOf(string key, string? prefix, string? delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
            return null;
        var start = prefix?.Length ?? 0;
        var idx = key.IndexOf(delimiter, start, StringComparison.Ordinal);
        return idx < 0 ? null : key[..(idx + delimiter.Length)];
    }

    // The smallest string above every string that starts with s; null where a surrogate makes that unsafe.
    private static string? Successor(string s)
    {
        var next = s[^1] + 1;
        return char.IsSurrogate(s[^1]) || next > char.MaxValue || char.IsSurrogate((char)next)
            ? null
            : s[..^1] + (char)next;
    }

    private static IQueryable<BlobObject> KeysFrom(ObjeXDbContext ctx, IQueryable<BlobObject> query, string bound, bool inclusive) => (IsPostgreSql(ctx), inclusive) switch
    {
        (true, true) => query.Where(o => string.Compare(EF.Functions.Collate(o.Key, "C"), bound) >= 0),
        (true, false) => query.Where(o => string.Compare(EF.Functions.Collate(o.Key, "C"), bound) > 0),
        (false, true) => query.Where(o => string.Compare(o.Key, bound) >= 0),
        (false, false) => query.Where(o => string.Compare(o.Key, bound) > 0),
    };

    public async Task<IReadOnlyList<BlobObject>> SearchObjectsAsync(string bucketName, string? prefix, string term, int limit, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var query = ctx.BlobObjects.AsNoTracking()
            .Where(o => o.BucketName == bucketName)
            .Where(KeyMatches(ctx, term))
            .Where(o => !o.Key.EndsWith("/"));
        if (!string.IsNullOrEmpty(prefix))
            query = query.Where(o => o.Key.StartsWith(prefix));

        return await OrderByKey(ctx, query).Take(limit).ToListAsync(ctk);
    }

    public async Task<IReadOnlyList<BlobObject>> SearchAllObjectsAsync(string? ownerFilter, string term, int limit, bool bestFirst = false, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        if (string.IsNullOrWhiteSpace(term)) return [];

        var query = ctx.BlobObjects.AsNoTracking()
            .Where(KeyMatches(ctx, term))
            .Where(o => !o.Key.EndsWith("/"));
        // The navigation joins Bucket, which is where ownership lives.
        if (ownerFilter is not null)
            query = query.Where(o => o.Bucket!.OwnerId == ownerFilter);

        var ordered = bestFirst ? OrderByBestMatch(ctx, query, term) : OrderByBucketThenKey(ctx, query);
        return await ordered.Take(limit).ToListAsync(ctk);
    }

    // Every word must match. First in the WHERE clause: it rejects most keys, so the other tests run on few.
    private static Expression<Func<BlobObject, bool>> KeyMatches(ObjeXDbContext ctx, string term) =>
        SearchPattern.Words(term.ToLowerInvariant())
            .Select(word => KeyLikeAny(ctx, SearchPattern.FromWordInBothForms(word)))
            .Aggregate((a, b) => Join(a, b, Expression.AndAlso));

    // PostgreSQL's LIKE is case-sensitive, so the key is lower-cased there, matching the trigram index on lower(key).
    // SQLite's LIKE already ignores ASCII case and its lower() folds nothing else, so there lower() would only cost time.
    private static Expression<Func<BlobObject, bool>> KeyLikeAny(ObjeXDbContext ctx, IEnumerable<string> patterns) =>
        patterns
            .Select(pattern => IsPostgreSql(ctx)
                ? (Expression<Func<BlobObject, bool>>)(o => EF.Functions.Like(o.Key.ToLower(), pattern, "\\"))
                : o => EF.Functions.Like(o.Key, pattern, "\\"))
            .Aggregate((a, b) => Join(a, b, Expression.OrElse));

    private static Expression<Func<BlobObject, bool>> Join(
        Expression<Func<BlobObject, bool>> a, Expression<Func<BlobObject, bool>> b, Func<Expression, Expression, BinaryExpression> join) =>
        Expression.Lambda<Func<BlobObject, bool>>(join(a.Body, ReplacingExpressionVisitor.Replace(b.Parameters[0], a.Parameters[0], b.Body)), a.Parameters);

    // S3 orders keys by UTF-8 bytes; SQLite's default BINARY collation already does that,
    // PostgreSQL needs COLLATE "C" because a locale collation sorts "a" before "B".
    private static IOrderedQueryable<BlobObject> OrderByKey(ObjeXDbContext ctx, IQueryable<BlobObject> query) =>
        IsPostgreSql(ctx)
            ? query.OrderBy(o => EF.Functions.Collate(o.Key, "C"))
            : query.OrderBy(o => o.Key);

    private static IOrderedQueryable<BlobObject> OrderByBucketThenKey(ObjeXDbContext ctx, IQueryable<BlobObject> query) =>
        IsPostgreSql(ctx)
            ? query.OrderBy(o => EF.Functions.Collate(o.BucketName, "C")).ThenBy(o => EF.Functions.Collate(o.Key, "C"))
            : query.OrderBy(o => o.BucketName).ThenBy(o => o.Key);

    private static IOrderedQueryable<BlobObject> ThenByBucketThenKey(ObjeXDbContext ctx, IOrderedQueryable<BlobObject> query) =>
        IsPostgreSql(ctx)
            ? query.ThenBy(o => EF.Functions.Collate(o.BucketName, "C")).ThenBy(o => EF.Functions.Collate(o.Key, "C"))
            : query.ThenBy(o => o.BucketName).ThenBy(o => o.Key);

    // "b/invoice.pdf" before "invoices/2024/a.pdf" before "x/my-invoice.pdf": a word that starts the key or a segment
    // ranks first, then the shorter key. Ranking needs every match, so the scan no longer stops at the limit.
    private static IOrderedQueryable<BlobObject> OrderByBestMatch(ObjeXDbContext ctx, IQueryable<BlobObject> query, string term)
    {
        var starts = SearchPattern.SegmentStarts(term.ToLowerInvariant());
        var ranked = starts.Count == 0
            ? query.OrderBy(o => o.Key.Length)
            : query.OrderBy(StartsFirst(KeyLikeAny(ctx, starts))).ThenBy(o => o.Key.Length);
        return ThenByBucketThenKey(ctx, ranked);
    }

    private static Expression<Func<BlobObject, int>> StartsFirst(Expression<Func<BlobObject, bool>> match) =>
        Expression.Lambda<Func<BlobObject, int>>(Expression.Condition(match.Body, Expression.Constant(0), Expression.Constant(1)), match.Parameters);

    private static bool IsPostgreSql(ObjeXDbContext ctx) => ctx.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    public async Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        return await ctx.BlobObjects.AsNoTracking().ToListAsync(ctk);
    }

    public async Task DeleteObjectAsync(string bucketName, string key, string? auditUserId = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        await using var tx = await BeginBucketWriteAsync(ctx, bucketName, ctk);
        var obj = await ctx.BlobObjects.FirstOrDefaultAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
        if (obj is not null)
        {
            ctx.BlobObjects.Remove(obj);
            if (auditUserId is not null)
                ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "DeleteObject", BucketName = bucketName, Key = key });

            await ctx.SaveChangesAsync(ctk);
            await AdjustBucketStatsAsync(ctx, bucketName, -1, -obj.Size, ctk);
            await tx.CommitAsync(ctk);
        }
    }

    public async Task<int> DeleteObjectsAsync(string bucketName, IEnumerable<string> keys, string? auditUserId = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        var keyList = keys.Distinct().ToList();
        if (keyList.Count == 0) return 0;

        await using var tx = await BeginBucketWriteAsync(ctx, bucketName, ctk);
        var objects = await ctx.BlobObjects
            .Where(o => o.BucketName == bucketName && keyList.Contains(o.Key))
            .ToListAsync(ctk);
        if (objects.Count == 0) return 0;

        ctx.BlobObjects.RemoveRange(objects);
        if (auditUserId is not null)
            foreach (var obj in objects)
                ctx.AuditEntries.Add(new AuditEntry { UserId = auditUserId, Action = "DeleteObject", BucketName = bucketName, Key = obj.Key });

        await ctx.SaveChangesAsync(ctk);
        await AdjustBucketStatsAsync(ctx, bucketName, -objects.Count, -objects.Sum(o => o.Size), ctk);
        await tx.CommitAsync(ctk);

        return objects.Count;
    }

    public async Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        return await ctx.BlobObjects
            .AnyAsync(o => o.BucketName == bucketName && o.Key == key, ctk);
    }

    /// <summary>
    /// Starts a write before anything of the bucket is read, so the stored size a delta is computed from cannot change until the commit:
    /// SQLite begins with BEGIN IMMEDIATE and holds the write lock, PostgreSQL locks the bucket row that every write updates.
    /// Each later statement on PostgreSQL reads with a fresh snapshot and so sees what the previous writer committed.
    /// </summary>
    private static async Task<IDbContextTransaction> BeginBucketWriteAsync(ObjeXDbContext ctx, string bucketName, CancellationToken ctk)
    {
        var tx = await ctx.Database.BeginTransactionAsync(ctk);
        if (IsPostgreSql(ctx))
            await ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM buckets WHERE name = {bucketName} FOR UPDATE", ctk);
        return tx;
    }

    /// <summary>Set-based so two writers on the same bucket cannot lose each other's delta.</summary>
    private static Task AdjustBucketStatsAsync(ObjeXDbContext ctx, string bucketName, long countDelta, long sizeDelta, CancellationToken ctk) =>
        ctx.Buckets
            .Where(b => b.Name == bucketName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.ObjectCount, b => b.ObjectCount + countDelta)
                .SetProperty(b => b.TotalSize, b => b.TotalSize + sizeDelta)
                .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ctk);

    public async Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        await using var tx = await BeginBucketWriteAsync(ctx, bucketName, ctk);
        await ctx.Buckets
            .Where(b => b.Name == bucketName)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.ObjectCount, b => ctx.BlobObjects.Count(o => o.BucketName == b.Name))
                .SetProperty(b => b.TotalSize, b => ctx.BlobObjects.Where(o => o.BucketName == b.Name).Sum(o => (long?)o.Size) ?? 0)
                .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ctk);
        await tx.CommitAsync(ctk);
    }

    public async Task<IEnumerable<ContentTypeStats>> GetContentTypeStatsAsync(IEnumerable<string>? bucketNames = null, CancellationToken ctk = default)
    {
        await using var ctx = await contexts.CreateDbContextAsync(ctk);
        var query = ctx.BlobObjects.Where(o => !o.Key.EndsWith("/"));
        if (bucketNames is not null)
        {
            var names = bucketNames.ToList();
            query = query.Where(o => names.Contains(o.BucketName));
        }
        return await query
            .GroupBy(o => o.ContentType)
            .Select(g => new ContentTypeStats(g.Key, g.Count(), g.Sum(o => o.Size)))
            .ToListAsync(ctk);
    }

    static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}