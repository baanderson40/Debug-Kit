using System.Globalization;
using System.Text;

namespace DebugKit.Core;

/// <summary>Bounded, single-line in-game display names. Names are explicitly included at the user's request.</summary>
public static class ObjectNames
{
    public const int MaximumLength = 128;

    public static string? For(ObjectCategory kind, string? displayName)
    {
        if (!Enum.IsDefined(kind) || kind == ObjectCategory.Any || string.IsNullOrWhiteSpace(displayName)) return null;

        var result = new StringBuilder(MaximumLength);
        // Bound traversal and output independently; discard line breaks, markup-like separators and bidi controls.
        foreach (var rune in displayName.AsSpan(0, Math.Min(512, displayName.Length)).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format || Rune.IsWhiteSpace(rune))
            {
                if (result.Length > 0 && result[^1] != ' ' && result.Length < MaximumLength) result.Append(' ');
                continue;
            }
            if (rune.Value is '|' or '"')
            {
                if (result.Length < MaximumLength) result.Append(' ');
                continue;
            }
            if (result.Length + rune.Utf16SequenceLength > MaximumLength) break;
            result.Append(rune.ToString());
        }
        var name = result.ToString().Trim();
        return name.Length == 0 ? null : name;
    }
}
