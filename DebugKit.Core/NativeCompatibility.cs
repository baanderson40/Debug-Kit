using System.Text.RegularExpressions;

namespace DebugKit.Core;

public static class NativeCompatibility
{
    // Exact inspected development baseline, NOT a claim of in-game validation.
    public const string GameBuild = "2026.11.03.0000.0000";
    public const string DalamudVersion = "15.0.4.0";
    public const string StructsHash = "28488f18ad05fdcd12373a83756eab275c291b09c6af061ef7f846aa06a36e29";
    public static string SafeBuild(string? build) => build is { Length: <= 32 } &&
        Regex.IsMatch(build, @"\A\d{4}\.\d{2}\.\d{2}\.\d{4}\.\d{4}\z", RegexOptions.CultureInvariant)
        ? build : "Unavailable";
    public static bool Matches(string? build, string? dalamud, string? hash) =>
        build == GameBuild && dalamud == DalamudVersion && string.Equals(hash, StructsHash, StringComparison.OrdinalIgnoreCase);
}
