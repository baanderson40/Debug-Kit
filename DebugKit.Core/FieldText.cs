using System.Globalization;
using System.Text;

namespace DebugKit.Core;

/// <summary>Evidence text policy, independent of display-name policy. Length limits never silently discard tails.</summary>
public static class FieldText
{
    public const int MaximumLength = 128;
    private const int MaximumExamined = 512;

    public static FieldRow Create(string field, string? value)
    {
        if (string.IsNullOrEmpty(value)) return new(ReportText.Clean(field), "unavailable");
        var text = new StringBuilder(MaximumLength);
        var shortened = value.Length > MaximumExamined;
        var examined = Math.Min(MaximumExamined, value.Length);
        if (examined < value.Length && char.IsHighSurrogate(value[examined - 1]) && char.IsLowSurrogate(value[examined])) examined--;
        foreach (var rune in value.AsSpan(0, examined).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format || Rune.IsWhiteSpace(rune) || rune.Value is '|' or '"')
            {
                if (text.Length > 0 && text[^1] != ' ' && text.Length < MaximumLength) text.Append(' ');
                continue;
            }
            if (text.Length + rune.Utf16SequenceLength > MaximumLength) { shortened = true; break; }
            text.Append(rune.ToString());
        }
        var clean = text.ToString().Trim();
        return new(ReportText.Clean(field), clean.Length == 0 ? "unavailable" : clean, shortened, shortened ? value.Length : null);
    }

    public static FieldRow Sanitize(FieldRow row)
    {
        var clean = Create(row.Field, row.Value);
        var shortened = row.TextShortened || clean.TextShortened;
        var original = row.TextShortened && row.OriginalTextLength is >= 0
            ? Math.Max(row.OriginalTextLength.Value, clean.OriginalTextLength ?? row.Value?.Length ?? 0)
            : clean.OriginalTextLength;
        return clean with { TextShortened = shortened, OriginalTextLength = shortened ? original : null };
    }

    public static string Display(FieldRow row) => row.TextShortened
        ? $"{row.Value} [text shortened; source {row.OriginalTextLength?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} UTF-16 units]" : row.Value;
}
