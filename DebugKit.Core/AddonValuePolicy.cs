namespace DebugKit.Core;

/// <summary>Reviewed semantic labels/decoders for known addon AtkValue slots.</summary>
public static class AddonValuePolicy
{
    public static bool IsReviewedString(string addon, int index) =>
        string.Equals(addon, "SelectYesno", StringComparison.Ordinal) && index is >= 0 and <= 3;

    public static string? FieldLabel(string addon, int index) =>
        string.Equals(addon, "SelectYesno", StringComparison.Ordinal) ? index switch
        {
            0 => "Prompt text",
            1 => "Button 1 text",
            2 => "Button 2 text",
            3 => "Button 3 text",
            _ => null,
        } : null;
}
