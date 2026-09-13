namespace ObjeX.Api.Options;

/// <summary>Credentials of the admin account created on first start when no such user exists.</summary>
public sealed class DefaultAdminOptions
{
    public const string SectionName = "DefaultAdmin";

    public string Username { get; set; } = "admin";
    public string Email { get; set; } = "admin@objex.local";
    public string Password { get; set; } = "admin";
}
