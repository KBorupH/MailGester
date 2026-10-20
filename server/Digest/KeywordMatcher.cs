namespace MailGester.Digest;

/// <summary>
/// Deterministic safety net next to the model. Sender is left out on purpose,
/// otherwise a term like ".NET" would match every .net domain.
/// </summary>
public static class KeywordMatcher
{
    public static bool Matches(MailItem mail, IReadOnlyList<string> keywords) =>
        keywords.Any(k =>
            mail.Subject.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            mail.Snippet.Contains(k, StringComparison.OrdinalIgnoreCase));
}
