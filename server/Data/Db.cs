using Microsoft.Data.Sqlite;

namespace MailGester.Data;

public sealed class DataPaths(IConfiguration config)
{
    public string Root { get; } = config["DataDirectory"] ?? "/data";
    public string Database => Path.Combine(Root, "mailgester.db");
    public string MsalCache => Path.Combine(Root, "msal.cache");
    public string Keys => Path.Combine(Root, "keys");
}

public sealed class Db(DataPaths paths)
{
    private readonly DataPaths _paths = paths;
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = paths.Database,
        ForeignKeys = true
    }.ToString();

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.Root);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS kv (
                key TEXT PRIMARY KEY,
                value BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL CHECK (kind IN ('gmail', 'outlook')),
                address TEXT NOT NULL UNIQUE COLLATE NOCASE,
                secret BLOB NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                cursor_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS digests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_utc TEXT NOT NULL,
                window_from_utc TEXT NOT NULL,
                window_to_utc TEXT NOT NULL,
                mail_count INTEGER NOT NULL,
                junk_count INTEGER NOT NULL,
                omitted INTEGER NOT NULL,
                mailbox_count INTEGER NOT NULL,
                pinged INTEGER NOT NULL,
                ping_reason TEXT NOT NULL,
                urgency TEXT NOT NULL,
                summary TEXT NOT NULL,
                ai_failure TEXT NULL,
                model TEXT NOT NULL,
                fetch_errors TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS digest_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                digest_id INTEGER NOT NULL REFERENCES digests(id) ON DELETE CASCADE,
                kind TEXT NOT NULL,
                account TEXT NOT NULL,
                folder TEXT NOT NULL,
                is_junk INTEGER NOT NULL,
                sender TEXT NOT NULL,
                subject TEXT NOT NULL,
                received_utc TEXT NOT NULL,
                category TEXT NULL,
                why TEXT NULL,
                action TEXT NULL,
                phishing INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_digest_items_digest ON digest_items(digest_id);
            CREATE INDEX IF NOT EXISTS ix_digests_created ON digests(created_utc);
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}

public static class SqliteExtensions
{
    public static SqliteCommand Cmd(this SqliteConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>Always UTC with a fixed format, so the stored text sorts correctly as plain strings.</summary>
    public static string Iso(this DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    public static DateTimeOffset ParseIso(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
