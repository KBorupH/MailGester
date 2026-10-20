namespace MailGester;

public sealed record MailItem(
    string Account,
    string Folder,
    bool IsJunk,
    string From,
    string Subject,
    DateTimeOffset Received,
    string Snippet);

public sealed record SourceError(string Account, string Message);

/// <summary>Structured answer from the model. Property names match the JSON schema sent to Ollama.</summary>
public sealed record DigestResult(
    string Summary,
    bool Notify,
    string Urgency,
    string NotifyReason,
    List<NoteworthyItem> Noteworthy);

public sealed record NoteworthyItem(
    int Id,
    string Category,
    string Why,
    string Action,
    bool SuspectedPhishing);
