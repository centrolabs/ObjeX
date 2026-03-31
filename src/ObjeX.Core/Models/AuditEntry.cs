namespace ObjeX.Core.Models;

public class AuditEntry
{
    public long Id { get; init; }
    public required string UserId { get; init; }
    public required string Action { get; init; }
    public string? BucketName { get; init; }
    public string? Key { get; init; }
    public string? Details { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
