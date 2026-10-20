namespace MailGester;

/// <summary>Everything configurable from the UI. Stored as one encrypted JSON document.</summary>
public sealed class AppSettings
{
    public DigestOptions Digest { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public DiscordOptions Discord { get; set; } = new();
    public GraphOptions Graph { get; set; } = new();
}

public sealed class DigestOptions
{
    public bool SchedulerEnabled { get; set; } = true;
    public string TimeZone { get; set; } = "Europe/Copenhagen";

    /// <summary>Local wall clock time (HH:mm, in <see cref="TimeZone"/>) of the daily run.</summary>
    public string RunAt { get; set; } = "07:30";

    public int FirstRunLookbackHours { get; set; } = 24;

    /// <summary>Upper bound for catch up after downtime, so a long outage cannot blow the prompt budget.</summary>
    public int MaxCatchUpDays { get; set; } = 7;

    public int HistoryRetentionDays { get; set; } = 90;
    public int SnippetChars { get; set; } = 300;
    public int MaxMailsPerFolder { get; set; } = 150;
    public string Language { get; set; } = "English";

    /// <summary>Free text about the user and what matters to them. Goes into the system prompt.</summary>
    public string UserContext { get; set; } = "";

    /// <summary>Case insensitive terms checked in code, independent of the model's verdict.</summary>
    public string[] PriorityKeywords { get; set; } =
    [
        "recruiter", "headhunter", "rekrutter", "stilling", "jobtilbud", "jobsamtale",
        "opportunity", "interview", "vacancy", "open role", "your profile", "din profil", "ansøgning"
    ];

    /// <summary>Ping even if the model stays quiet when a priority keyword shows up in a junk folder.</summary>
    public bool PingOnJunkKeywordHit { get; set; } = true;
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://ollama:11434";
    public string Model { get; set; } = "qwen2.5:14b";
    public int NumCtx { get; set; } = 32768;
    public double Temperature { get; set; } = 0.2;
    public int TimeoutMinutes { get; set; } = 20;
}

public sealed class DiscordOptions
{
    public string WebhookUrl { get; set; } = "";

    /// <summary>Role that gets mentioned when the digest is worth interrupting for.</summary>
    public string PingRoleId { get; set; } = "";
}

public sealed class GraphOptions
{
    public string ClientId { get; set; } = "";
    public string Authority { get; set; } = "https://login.microsoftonline.com/consumers";
}

public enum AccountKind
{
    Gmail,
    Outlook
}

public sealed class MailAccount
{
    public long Id { get; set; }
    public AccountKind Kind { get; set; }
    public string Address { get; set; } = "";

    /// <summary>Gmail app password. Null for Outlook, whose tokens live in the MSAL cache.</summary>
    public string? Secret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>High water mark of the last successful fetch, so a failing account resumes where it stopped.</summary>
    public DateTimeOffset? Cursor { get; set; }
}
