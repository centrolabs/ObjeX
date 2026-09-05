namespace ObjeX.Api.Options;

/// <summary>
/// Login protection. Lockout is per account and counts failed attempts only, enforced by ASP.NET Core
/// Identity and persisted in the user row. There is deliberately no IP-based limiting: behind CGNAT or a
/// shared proxy one address is many people, and a per-IP window punishes all of them for one attacker.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public LockoutSettings Lockout { get; set; } = new();

    public sealed class LockoutSettings
    {
        /// <summary>Failed attempts that lock the account.</summary>
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>How long the account stays locked.</summary>
        public int DurationMinutes { get; set; } = 5;
    }

    public void Validate()
    {
        if (Lockout.MaxFailedAttempts < 1)
            throw new InvalidOperationException($"Auth:Lockout:MaxFailedAttempts must be at least 1 (got {Lockout.MaxFailedAttempts}).");
        if (Lockout.DurationMinutes < 1)
            throw new InvalidOperationException($"Auth:Lockout:DurationMinutes must be at least 1 (got {Lockout.DurationMinutes}).");
    }
}
