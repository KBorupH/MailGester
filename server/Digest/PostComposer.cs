using MailGester.Sources;

namespace MailGester.Digest;

/// <summary>
/// Turns the model's judgement into a Discord post. Sender and subject always come from the original mail,
/// never from the model, and everything mail derived is neutralized so it cannot render as a clickable link.
/// </summary>
public static class PostComposer
{
    private const int MaxNoteworthy = 10;
    private const int MaxKeywordLines = 8;

    public static DigestPost Compose(
        DigestResult? ai,
        string? aiFailure,
        IReadOnlyList<MailItem> shown,
        IReadOnlyList<MailItem> keywordHits,
        IReadOnlyList<SourceError> errors,
        int omitted,
        int mailboxCount,
        DigestOptions options,
        string model)
    {
        var reasons = new List<string>();

        if (ai is { Notify: true })
        {
            reasons.Add(ai.NotifyReason.Length > 0 ? ai.NotifyReason : "The digest has something worth a look.");
        }
        else if (options.PingOnJunkKeywordHit && keywordHits.Any(m => m.IsJunk))
        {
            reasons.Add("A priority keyword matched in a junk folder, check it.");
        }

        if (aiFailure is not null)
        {
            reasons.Add("The AI digest failed, showing keyword matches only.");
        }

        if (errors.Count > 0)
        {
            reasons.Add($"{errors.Count} mailbox(es) could not be read.");
        }

        var fields = new List<PostField>();

        if (ai is not null)
        {
            foreach (var item in ai.Noteworthy.Where(n => n.Id >= 1 && n.Id <= shown.Count).DistinctBy(n => n.Id).Take(MaxNoteworthy))
            {
                fields.Add(NoteworthyField(item, shown[item.Id - 1]));
            }
        }

        if (keywordHits.Count > 0)
        {
            var lines = keywordHits.Take(MaxKeywordLines).Select(m =>
                $"• {(m.IsJunk ? "**JUNK** " : "")}{Code(m.Subject, 80)} ({m.Account})");
            var more = keywordHits.Count > MaxKeywordLines ? $"\n…and {keywordHits.Count - MaxKeywordLines} more" : "";
            fields.Add(new PostField("Keyword matches", string.Join("\n", lines) + more));
        }

        if (errors.Count > 0)
        {
            fields.Add(new PostField(
                "Fetch problems",
                string.Join("\n", errors.Select(e => $"• {e.Account}: {Neutralize(TextCleaner.Truncate(e.Message, 160))}"))));
        }

        var junkCount = shown.Count(m => m.IsJunk);
        var footer = $"{shown.Count + omitted} mails from {mailboxCount} mailboxes, {junkCount} in junk"
            + (omitted > 0 ? $", {omitted} not analysed" : "")
            + $" · {model}";

        var description = aiFailure is not null
            ? $"The AI digest failed after several attempts: {Neutralize(TextCleaner.Truncate(aiFailure, 200))}"
            : Neutralize(ai?.Summary ?? "No new mail since the last digest.");

        var ping = reasons.Count > 0;
        return new DigestPost(
            ping,
            string.Join(" ", reasons),
            ping ? "Mail digest: needs a look" : "Mail digest",
            description,
            fields,
            footer);
    }

    private static PostField NoteworthyField(NoteworthyItem item, MailItem mail)
    {
        var name = $"{item.Category} · {mail.Account}{(mail.IsJunk ? " · JUNK" : "")}";
        var lines = new List<string>
        {
            $"**{Code(mail.Subject, 100)}**",
            $"From {Code(mail.From, 80)}",
            Neutralize(item.Why),
            $"Action: {item.Action}"
        };

        if (item.SuspectedPhishing)
        {
            lines.Add("⚠ Possible phishing, do not click links.");
        }

        return new PostField(name, string.Join("\n", lines));
    }

    /// <summary>Inline code prevents markdown links from rendering, and backticks are removed so it cannot be broken out of.</summary>
    private static string Code(string text, int max) =>
        "`" + Neutralize(TextCleaner.Truncate(text.Replace("`", "'"), max)) + "`";

    /// <summary>Breaks URL autolinking and masked links with a zero width space.</summary>
    private static string Neutralize(string text) =>
        text.Replace("://", ":/​/").Replace("](", "]​(");
}
