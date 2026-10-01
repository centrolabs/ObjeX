using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

/// <summary>An Admin's schedule for a recurring job. Without a row the job runs on its default schedule.</summary>
public class JobSchedule : IHasTimestamps
{
    public required string JobId { get; init; }
    public required string Cron { get; set; }
    /// <summary>The zone the cron is read in, so a local time stays the same across daylight saving time.</summary>
    public required string TimeZone { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
