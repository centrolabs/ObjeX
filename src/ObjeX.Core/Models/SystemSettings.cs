namespace ObjeX.Core.Models;

public class SystemSettings
{
    public int Id { get; init; } = 1; // always 1 — singleton row
    public int PresignedUrlDefaultExpirySeconds { get; set; } = 3600;
    public int PresignedUrlMaxExpirySeconds { get; set; } = 604800;
    public long? DefaultStorageQuotaBytes { get; set; } // null = unlimited
}
