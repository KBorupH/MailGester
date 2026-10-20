namespace MailGester.Data;

public sealed class KvStore(Db db)
{
    private readonly Db _db = db;

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd("SELECT value FROM kv WHERE key = $key", ("$key", key));
        return await command.ExecuteScalarAsync(ct) as byte[];
    }

    public async Task SetAsync(string key, byte[] value, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            """
            INSERT INTO kv (key, value) VALUES ($key, $value)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value
            """,
            ("$key", key), ("$value", value));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetTextAsync(string key, CancellationToken ct = default) =>
        await GetAsync(key, ct) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    public Task SetTextAsync(string key, string value, CancellationToken ct = default) =>
        SetAsync(key, System.Text.Encoding.UTF8.GetBytes(value), ct);
}
