using MailGester.Sources;

namespace MailGester.Digest;

/// <summary>
/// Turns the model's judgement into a Discord post. Sender and subject always come from the original mail,
/// never from the model, and everything mail derived is neutralized so it cannot render as a clickable link.
/// </summary>
public static class PostComposer
{
    private const int MaxNoteworthy = 5;
    private const int MaxKeywordLines = 8;

    /// <summary>Flagged mails worth showing, and flagged mails kept out of Discord but still stored in history.</summary>
    public sealed record Selection(
        IReadOnlyList<(NoteworthyItem Item, MailItem Mail)> Items,
        IReadOnlyList<(NoteworthyItem Item, MailItem Mail)> Hidden);

    /// <summary>
    /// Applies the display rules in code, because a small model does not reliably leave obvious junk out of the list.
    /// Suspected phishing and items that need no action and anything beyond the cap are moved to <see cref="Selection.Hidden"/>.
    /// </summary>
    public static Selection Select(DigestResult? ai, IReadOnlyList<MailItem> shown)
    {
        if (ai is null)
        {
            return new Selection([], []);
        }

        var valid = ai.Noteworthy
            .Where(n => n.Id >= 1 && n.Id <= shown.Count)
            .DistinctBy(n => n.Id)
            .ToList();

        var visible = valid
            .Where(n => !n.SuspectedPhishing && !n.Action.Equals("none", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var items = visible.Take(MaxNoteworthy).Select(n => (Item: n, Mail: shown[n.Id - 1])).ToList();
        var shownIds = items.Select(i => i.Item.Id).ToHashSet();
        var hidden = valid.Where(n => !shownIds.Contains(n.Id)).Select(n => (Item: n, Mail: shown[n.Id - 1])).ToList();

        return new Selection(items, hidden);
    }

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
        var selection = Select(ai, shown);
        var shownMails = selection.Items.Select(i => i.Mail).ToHashSet();
        var extraHits = keywordHits.Where(m => !shownMails.Contains(m)).ToList();

        // A ping needs something to point at, so a model ping with nothing listed is ignored.
        if (ai is { Notify: true } && (selection.Items.Count > 0 || keywordHits.Count > 0))
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

        foreach (var (item, mail) in selection.Items)
        {
            fields.Add(NoteworthyField(item, mail));
        }

        if (extraHits.Count > 0)
        {
            var lines = extraHits.Take(MaxKeywordLines).Select(m =>
                $"• {(m.IsJunk ? "**JUNK** " : "")}{Code(m.Subject, 80)} ({m.Account})");
            var more = extraHits.Count > MaxKeywordLines ? $"\n…and {extraHits.Count - MaxKeywordLines} more" : "";
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
            + (selection.Hidden.Count > 0 ? $", {selection.Hidden.Count} hidden (see History)" : "")
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

        return new PostField(name, string.Join("\n", lines));
    }

    /// <summary>Inline code prevents markdown links from rendering, and backticks are removed so it cannot be broken out of.</summary>
    private static string Code(string text, int max) =>
        "`" + Neutralize(TextCleaner.Truncate(text.Replace("`", "'"), max)) + "`";

    /// <summary>Breaks URL autolinking and masked links with a zero width space.</summary>
    private static string Neutralize(string text) =>
        text.Replace("://", ":/​/").Replace("](", "]​(");
}
