namespace ObjeX.Api.Options;

/// <summary>
/// Database provider and connection, validated once at startup. SQLite is the default; PostgreSQL is
/// opted into with Database:Provider=postgresql (or the DATABASE_PROVIDER env var) and a PostgreSQL
/// connection string.
/// </summary>
public sealed class DatabaseOptions
{
    public const string Sqlite = "sqlite";
    public const string PostgreSql = "postgresql";

    public string Provider { get; private init; } = Sqlite;
    public string ConnectionString { get; private init; } = "";

    /// <summary>Absolute SQLite file path (EF Core and Hangfire open the same file). Null for PostgreSQL.</summary>
    public string? SqliteFilePath { get; private init; }

    public bool AutoMigrate { get; private init; } = true;

    public bool IsSqlite => Provider == Sqlite;
    public bool IsPostgreSql => Provider == PostgreSql;

    public static DatabaseOptions Load(IConfiguration configuration, Func<string, string> resolvePath)
    {
        var provider = (configuration["Database:Provider"] ?? configuration["DATABASE_PROVIDER"] ?? Sqlite).ToLowerInvariant();
        if (provider is not (Sqlite or PostgreSql))
            throw new InvalidOperationException(
                $"Invalid database provider '{provider}'. Supported values: sqlite, postgresql. Set via DATABASE_PROVIDER env var or Database:Provider in config.");

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        string? sqliteFilePath = null;

        if (provider == Sqlite)
        {
            connectionString ??= "Data Source=data/db/objex.db";
            sqliteFilePath = resolvePath(connectionString.Replace("Data Source=", "").Trim());
            connectionString = $"Data Source={sqliteFilePath}";
        }
        else if (string.IsNullOrEmpty(connectionString) || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection must be a PostgreSQL connection string when Database:Provider=postgresql. " +
                "Example: Host=localhost;Database=objex;Username=objex;Password=secret");
        }

        return new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            SqliteFilePath = sqliteFilePath,
            AutoMigrate = configuration.GetValue("Database:AutoMigrate", defaultValue: true),
        };
    }
}
