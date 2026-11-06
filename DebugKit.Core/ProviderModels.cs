using System.Collections.Immutable;

namespace DebugKit.Core;

public enum ProviderKind { AutoRetainer, Lifestream, Vnavmesh, AutoDuty, BossMod, BossModReborn, RotationSolver, WrathCombo, WahTools }
public enum ObservationState { Unknown, Yes, No, Ambiguous }
// Older states remain readable for historical v1 reports; new captures use ReviewedEndpoint, not version certification.
public enum ContractState { NotReviewed, UnsupportedVersion, Supported, ReviewedEndpoint }
public enum ProviderVersionEvidence { Unknown, MatchesReviewedSource, DiffersFromReviewedSource }
public enum ProviderQuery { Suppressed, Busy, NavmeshReady, IpcReady }
public sealed record ProviderDefinition(ProviderKind Kind, string InternalName);
public sealed record ProviderMetadataSample(string InternalName, string Version, bool Loaded, bool Dev);
public sealed record ProviderRow(ProviderKind Provider, ObservationState Installed, ObservationState Loaded,
    string? Version, ContractState Contract, ObservationState EndpointPresent = ObservationState.Unknown,
    ObservationState Responding = ObservationState.Unknown, bool? Ready = null, bool DevelopmentBuild = false)
{
    public string? ReviewedSourceVersion => ProviderCatalog.Contract(this)?.ReviewedSourceVersion;
    public ProviderVersionEvidence VersionEvidence => Version == null || ReviewedSourceVersion == null ? ProviderVersionEvidence.Unknown
        : Version == ReviewedSourceVersion ? ProviderVersionEvidence.MatchesReviewedSource : ProviderVersionEvidence.DiffersFromReviewedSource;
}
public sealed record ProviderPayload(ImmutableArray<ProviderRow> Rows, bool EnumerationComplete, string Source) : ToolPayload;
public sealed record ProviderQueryPayload(ProviderRow Provider, ProviderQuery Query, bool? Value, string Endpoint) : ToolPayload;
public sealed record ProviderContract(ProviderKind Provider, string ReviewedSourceVersion, ProviderQuery Query, string Endpoint);

public static class ProviderCatalog
{
    public static ImmutableArray<ProviderContract> Contracts { get; } =
    [
        new(ProviderKind.AutoRetainer, "4.6.1.26", ProviderQuery.Suppressed, "AutoRetainer.GetSuppressed"),
        new(ProviderKind.Lifestream, "2.5.4.18", ProviderQuery.Busy, "Lifestream.IsBusy"),
        new(ProviderKind.Vnavmesh, "1.2.3.14", ProviderQuery.NavmeshReady, "vnavmesh.Nav.IsReady"),
        new(ProviderKind.WrathCombo, "1.0.4.27", ProviderQuery.IpcReady, "WrathCombo.IPCReady"),
    ];

    // Metadata versions/dev flags are evidence only: fixed endpoint selection never depends on them.
    public static ProviderContract? Contract(ProviderRow row) => Contracts.FirstOrDefault(x => x.Provider == row.Provider);
    // Exact internal names, never display-name/fuzzy matches. No unrelated plugin inventory retained.
    public static ImmutableArray<ProviderDefinition> All { get; } =
    [
        new(ProviderKind.AutoRetainer, "AutoRetainer"), new(ProviderKind.Lifestream, "Lifestream"),
        new(ProviderKind.Vnavmesh, "vnavmesh"), new(ProviderKind.AutoDuty, "AutoDuty"),
        new(ProviderKind.BossMod, "BossMod"), new(ProviderKind.BossModReborn, "BossModReborn"),
        // Rotation Solver Reborn also uses RotationSolver. Internal name alone cannot identify its variant.
        new(ProviderKind.RotationSolver, "RotationSolver"),
        new(ProviderKind.WrathCombo, "WrathCombo"), new(ProviderKind.WahTools, "WahTools"),
    ];

    public static ProviderPayload Metadata(IEnumerable<ProviderMetadataSample> samples, bool complete)
    {
        var candidates = samples.Take(513).ToArray();
        complete &= candidates.Length <= 512;
        var bounded = candidates.Take(512).ToArray();
        return new(All.Select(definition =>
        {
            var matches = bounded.Where(x => x.InternalName == definition.InternalName).Take(2).ToArray();
            if (matches.Length > 1) return new ProviderRow(definition.Kind, ObservationState.Ambiguous,
                ObservationState.Ambiguous, null, ContractState.NotReviewed);
            if (matches.Length == 0) return new ProviderRow(definition.Kind,
                complete ? ObservationState.No : ObservationState.Unknown, ObservationState.Unknown, null, ContractState.NotReviewed);
            var match = matches[0];
            var row = new ProviderRow(definition.Kind, ObservationState.Yes, match.Loaded ? ObservationState.Yes : ObservationState.No,
                SafeVersion(match.Version), ContractState.NotReviewed, DevelopmentBuild: match.Dev);
            return row with { Contract = Contract(row) != null ? ContractState.ReviewedEndpoint : ContractState.NotReviewed };
        }).ToImmutableArray(), complete, "Public Dalamud InstalledPlugins metadata; no IPC invoked");
    }

    public static string? SafeVersion(string? version) => version is { Length: <= 32 } && Version.TryParse(version, out var parsed)
        ? parsed.ToString() : null;
}
