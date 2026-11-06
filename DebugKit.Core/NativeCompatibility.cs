using System.Text.RegularExpressions;

namespace DebugKit.Core;

public static class NativeCompatibility
{
    // Exact source-reviewed development baseline, NOT a claim of in-game validation.
    public const string GameBuild = "2026.10.16.0000.0000";
    public const string DalamudVersion = "15.0.3.9";
    public const string StructsHash = "f5a2c68ccecd3e199bda572050e98902b2e7594066c95dc7826c2d4297cd0564";
    public static string SafeBuild(string? build) => build is { Length: <= 32 } &&
        Regex.IsMatch(build, @"\A\d{4}\.\d{2}\.\d{2}\.\d{4}\.\d{4}\z", RegexOptions.CultureInvariant)
        ? build : "Unavailable";
    public static bool Matches(string? build, string? dalamud, string? hash) =>
        build == GameBuild && dalamud == DalamudVersion && string.Equals(hash, StructsHash, StringComparison.OrdinalIgnoreCase);
}
