using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MailGester.Data;

public sealed class DigestRecord
{
    public long Id { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset WindowFromUtc { get; set; }
    public DateTimeOffset WindowToUtc { get; set; }
    public int MailCount { get; set; }
    public int JunkCount { get; set; }
    public int Omitted { get; set; }
    public int MailboxCount { get; set; }
    public bool Pinged { get; set; }
    public string PingReason { get; set; } = "";
    public string Urgency { get; set; } = "none";
    public string Summary { get; set; } = "";
    public string? AiFailure { get; set; }
    public string Model { get; set; } = "";
    public List<SourceError> FetchErrors { get; set; } = [];
    public List<DigestItemRecord> Items { get; set; } = [];
}

/// <summary>Only noteworthy mails, keyword hits and mails hidden from Discord are kept, so history never holds the whole mailbox.</summary>
public sealed class DigestItemRecord
{
    public string Kind { get; set; } = "";
    public string Account { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool IsJunk { get; set; }
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset ReceivedUtc { get; set; }
    public string? Category { get; set; }
    public string? Why { get; set; }
    public string? Action { get; set; }
    public bool Phishing { get; set; }
}

public sealed class DigestRepository(Db db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Db _db = db;

    public async Task<long> InsertAsync(DigestRecord digest, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var insert = connection.Cmd(
            """
            INSERT INTO digests (
                created_utc, window_from_utc, window_to_utc, mail_count, junk_count, omitted, mailbox_count,
                pinged, ping_reason, urgency, summary, ai_failure, model, fetch_errors)
            VALUES (
                $created, $from, $to, $mails, $junk, $omitted, $mailboxes,
                $pinged, $reason, $urgency, $summary, $failure, $model, $errors)
            RETURNING id
            """,
            ("$created", digest.CreatedUtc.Iso()),
            ("$from", digest.WindowFromUtc.Iso()),
            ("$to", digest.WindowToUtc.Iso()),
            ("$mails", digest.MailCount),
            ("$junk", digest.JunkCount),
            ("$omitted", digest.Omitted),
            ("$mailboxes", digest.MailboxCount),
            ("$pinged", digest.Pinged),
            ("$reason", digest.PingReason),
            ("$urgency", digest.Urgency),
            ("$summary", digest.Summary),
            ("$failure", digest.AiFailure),
            ("$model", digest.Model),
            ("$errors", JsonSerializer.Serialize(digest.FetchErrors, Json)));
        insert.Transaction = transaction;

        var id = (long)(await insert.ExecuteScalarAsync(ct))!;

        foreach (var item in digest.Items)
        {
            await using var command = connection.Cmd(
                """
                INSERT INTO digest_items (
                    digest_id, kind, account, folder, is_junk, sender, subject, received_utc,
                    category, why, action, phishing)
                VALUES (
                    $digest, $kind, $account, $folder, $junk, $sender, $subject, $received,
                    $category, $why, $action, $phishing)
                """,
                ("$digest", id),
                ("$kind", item.Kind),
                ("$account", item.Account),
                ("$folder", item.Folder),
                ("$junk", item.IsJunk),
                ("$sender", item.Sender),
                ("$subject", item.Subject),
                ("$received", item.ReceivedUtc.Iso()),
                ("$category", item.Category),
                ("$why", item.Why),
                ("$action", item.Action),
                ("$phishing", item.Phishing));
            command.Transaction = transaction;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<List<DigestRecord>> ListAsync(int take, int skip, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd(
            $"""
            {SelectDigest}
            ORDER BY id DESC
            LIMIT $take OFFSET $skip
            """,
            ("$take", take), ("$skip", skip));

        var digests = new List<DigestRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            digests.Add(MapDigest(reader));
        }

        return digests;
    }

    public async Task<DigestRecord?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);

        DigestRecord? digest;
        await using (var command = connection.Cmd($"{SelectDigest} WHERE id = $id", ("$id", id)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            digest = await reader.ReadAsync(ct) ? MapDigest(reader) : null;
        }

        if (digest is null)
        {
            return null;
        }

        await using var items = connection.Cmd(
            """
            SELECT kind, account, folder, is_junk, sender, subject, received_utc, category, why, action, phishing
            FROM digest_items
            WHERE digest_id = $id
            ORDER BY id
            """,
            ("$id", id));

        await using var itemReader = await items.ExecuteReaderAsync(ct);
        while (await itemReader.ReadAsync(ct))
        {
            digest.Items.Add(new DigestItemRecord
            {
                Kind = itemReader.GetString(0),
                Account = itemReader.GetString(1),
                Folder = itemReader.GetString(2),
                IsJunk = itemReader.GetBoolean(3),
                Sender = itemReader.GetString(4),
                Subject = itemReader.GetString(5),
                ReceivedUtc = SqliteExtensions.ParseIso(itemReader.GetString(6)),
                Category = itemReader.IsDBNull(7) ? null : itemReader.GetString(7),
                Why = itemReader.IsDBNull(8) ? null : itemReader.GetString(8),
                Action = itemReader.IsDBNull(9) ? null : itemReader.GetString(9),
                Phishing = itemReader.GetBoolean(10)
            });
        }

        return digest;
    }

    public async Task PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var command = connection.Cmd("DELETE FROM digests WHERE created_utc < $cutoff", ("$cutoff", olderThan.Iso()));
        await command.ExecuteNonQueryAsync(ct);
    }

    private const string SelectDigest =
        """
        SELECT id, created_utc, window_from_utc, window_to_utc, mail_count, junk_count, omitted, mailbox_count,
               pinged, ping_reason, urgency, summary, ai_failure, model, fetch_errors
        FROM digests
        """;

    private static DigestRecord MapDigest(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        CreatedUtc = SqliteExtensions.ParseIso(reader.GetString(1)),
        WindowFromUtc = SqliteExtensions.ParseIso(reader.GetString(2)),
        WindowToUtc = SqliteExtensions.ParseIso(reader.GetString(3)),
        MailCount = reader.GetInt32(4),
        JunkCount = reader.GetInt32(5),
        Omitted = reader.GetInt32(6),
        MailboxCount = reader.GetInt32(7),
        Pinged = reader.GetBoolean(8),
        PingReason = reader.GetString(9),
        Urgency = reader.GetString(10),
        Summary = reader.GetString(11),
        AiFailure = reader.IsDBNull(12) ? null : reader.GetString(12),
        Model = reader.GetString(13),
        FetchErrors = JsonSerializer.Deserialize<List<SourceError>>(reader.GetString(14), Json) ?? []
    };
}
