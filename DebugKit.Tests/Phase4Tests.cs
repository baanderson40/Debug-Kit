using System.Collections.Immutable;
using DebugKit.Core;

internal static class Phase4Tests
{
    public static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("provider metadata distinguishes absent loaded unloaded and unknown", MetadataStates),
        ("provider metadata rejects ambiguous and fuzzy matches", MetadataIdentity),
        ("provider payload bounds versions and trims through shared pipeline", MetadataBounds),
        ("metadata options discard unrelated native and identity inputs", MetadataOptions),
        ("provider queries gate absence unloaded ambiguity and unreviewed endpoints", QueryGates),
        ("provider query preserves false distinguishes missing and sanitizes failures", QueryOutcomes),
        ("provider reload changes cancel rather than fabricating continuity", QueryReload),
        ("every approved provider contract stays fixed across metadata versions", ContractMatrix),
        ("metadata scan cap preserves unknowns and incomplete scans cannot invoke IPC", IncompleteMetadata),
        ("installed provider versions are advisory and do not block fixed getters", VersionAdvisory),
        ("provider version warnings survive text JSON and response failures", VersionReports),
    }.Concat(NetworkTests.All);
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void MetadataStates()
    {
        var p = ProviderCatalog.Metadata([new("AutoRetainer", "1.2.3.4", true, false), new("Lifestream", "2.0", false, false)], true);
        Check(p.Rows[0].Installed == ObservationState.Yes && p.Rows[0].Loaded == ObservationState.Yes);
        Check(p.Rows[1].Installed == ObservationState.Yes && p.Rows[1].Loaded == ObservationState.No);
        Check(p.Rows[2].Installed == ObservationState.No && p.Rows[2].Loaded == ObservationState.Unknown);
        Check(ProviderCatalog.Metadata([], false).Rows.All(x => x.Installed == ObservationState.Unknown));
        Check(p.Rows.All(x => x.EndpointPresent == ObservationState.Unknown && x.Responding == ObservationState.Unknown && x.Ready == null));
    }
    private static void MetadataIdentity()
    {
        var p = ProviderCatalog.Metadata([new("AutoRetainer", "1.0", false, false), new("AutoRetainer", "2.0", true, true), new("VNAVMESH", "1.0", true, false)], true);
        Check(p.Rows[0].Installed == ObservationState.Ambiguous && p.Rows[0].Version == null);
        Check(p.Rows[2].Installed == ObservationState.No);
    }
    private static void MetadataBounds()
    {
        var p = ProviderCatalog.Metadata([new("AutoRetainer", "secret path/token", true, false)], true);
        Check(p.Rows[0].Version == null && p.Rows.Length == ProviderCatalog.All.Length);
        Check(Payloads.Count(p) == p.Rows.Length && Payloads.Count(Payloads.TrimLast(p)) == p.Rows.Length - 1);
        Check(Payloads.Sanitize(p) is ProviderPayload && ProviderCatalog.SafeVersion(new string('1', 1000)) == null);
        Check(ProviderCatalog.SafeVersion("1.2.3.4") == "1.2.3.4");
    }
    private static void MetadataOptions()
    {
        var o = new ToolOptions(Addon: "ignored", ExperimentalNative: true, FilterId: 123);
        Check(o.IsValid(Inspector.ProviderMetadata) && o.Canonical(Inspector.ProviderMetadata) == new ToolOptions());
        Check(!Catalog.Get(Inspector.ProviderMetadata).RequiresPlayer && !Catalog.Get(Inspector.ProviderMetadata).BulkSafe);
    }
    private static ProviderPayload Supported(ProviderKind kind = ProviderKind.AutoRetainer) => ProviderCatalog.Metadata(
        [new(ProviderCatalog.All.First(x => x.Kind == kind).InternalName,
             ProviderCatalog.Contracts.First(x => x.Provider == kind).ReviewedSourceVersion, true, false)], true);
    private static void QueryGates()
    {
        var calls = 0;
        bool? Invoke(ProviderContract _) { calls++; return true; }
        Check(ProviderQueries.Capture(ProviderKind.AutoRetainer, () => ProviderCatalog.Metadata([], true), Invoke).Reason == Reason.ProviderUnavailable);
        Check(ProviderQueries.Capture(ProviderKind.AutoRetainer, () => ProviderCatalog.Metadata([new("AutoRetainer", "4.6.2.18", false, false)], true), Invoke).Reason == Reason.ProviderUnavailable);
        Check(ProviderQueries.Capture(ProviderKind.AutoRetainer, () => ProviderCatalog.Metadata([new("AutoRetainer", "4.6.2.18", true, false), new("AutoRetainer", "4.6.2.18", true, false)], true), Invoke).Reason == Reason.ProviderUnavailable);
        Check(ProviderQueries.Capture(ProviderKind.WahTools, () => ProviderCatalog.Metadata([new("WahTools", "20.0.0.29", true, false)], true), Invoke).Reason == Reason.UnsupportedContract);
        Check(calls == 0);
    }
    private static void QueryOutcomes()
    {
        var good = ProviderQueries.Capture(ProviderKind.AutoRetainer, () => Supported(), _ => false);
        Check(good.Outcome == Outcome.Success && good.Payload is ProviderQueryPayload { Value: false, Provider.Ready: null });
        var nav = ProviderQueries.Capture(ProviderKind.Vnavmesh, () => Supported(ProviderKind.Vnavmesh), _ => false);
        Check(nav.Payload is ProviderQueryPayload { Provider.Ready: false });
        Check(ProviderQueries.Capture(ProviderKind.AutoRetainer, () => Supported(), _ => null).Reason == Reason.IpcMissing);
        var failed = ProviderQueries.Capture(ProviderKind.AutoRetainer, () => Supported(), _ => throw new Exception("private secret"));
        Check(failed.Reason == Reason.IpcFailed && failed.Payload is ProviderQueryPayload { Value: null, Provider.Responding: ObservationState.No });
    }
    private static void QueryReload()
    {
        var n = 0;
        var step = ProviderQueries.Capture(ProviderKind.AutoRetainer, () => ++n == 1 ? Supported() : ProviderCatalog.Metadata([], true), _ => true);
        Check(step.Reason == Reason.SourceChanged && step.Payload == null);
    }
    private static void ContractMatrix()
    {
        Check(ProviderCatalog.Contracts.Length == 4);
        foreach (var contract in ProviderCatalog.Contracts)
        {
            var step = ProviderQueries.Capture(contract.Provider, () => Supported(contract.Provider), observed =>
            { Check(observed == contract); return false; });
            Check(step.Payload is ProviderQueryPayload { Value: false } p && p.Provider.Responding == ObservationState.Yes);
            var different = Supported(contract.Provider) with { Rows = Supported(contract.Provider).Rows.Select(r => r with { Version = "999.0.0.0" }).ToImmutableArray() };
            var response = ProviderQueries.Capture(contract.Provider, () => different, observed => { Check(observed == contract); return false; });
            Check(response.Outcome == Outcome.Success && response.Payload is ProviderQueryPayload { Value: false, Provider.VersionEvidence: ProviderVersionEvidence.DiffersFromReviewedSource });
        }
    }
    private static void IncompleteMetadata()
    {
        var bounded = ProviderCatalog.Metadata(Enumerable.Repeat(new ProviderMetadataSample("Unrelated", "1.0", false, false), 513), true);
        Check(!bounded.EnumerationComplete && bounded.Rows.All(x => x.Installed == ObservationState.Unknown));
        var partial = Supported() with { EnumerationComplete = false };
        Check(ProviderQueries.Capture(ProviderKind.AutoRetainer, () => partial, _ => throw new Exception("must not invoke")).Reason == Reason.ProviderUnavailable);
    }
    private static void VersionAdvisory()
    {
        var samples = new[]
        {
            new ProviderMetadataSample("AutoRetainer", "4.6.2.18", true, false),
            new("Lifestream", "2.6.0.12", true, false), new("vnavmesh", "1.2.3.15", true, false),
            new("AutoRetainer", "4.6.1.26", true, true), new("AutoRetainer", "unknown/private", true, false),
        };
        foreach (var sample in samples)
        {
            var metadata = ProviderCatalog.Metadata([sample], true);
            var row = metadata.Rows.First(x => x.Installed == ObservationState.Yes);
            Check(row.Contract == ContractState.ReviewedEndpoint && row.EndpointPresent == ObservationState.Unknown);
            var calls = 0;
            var response = ProviderQueries.Capture(row.Provider, () => metadata, endpoint =>
            { calls++; Check(endpoint.Endpoint == ProviderCatalog.Contracts.First(x => x.Provider == row.Provider).Endpoint); return true; });
            Check(calls == 1 && response.Outcome == Outcome.Success);
        }
        Check(ProviderCatalog.Metadata([samples[0]], true).Rows[0].VersionEvidence == ProviderVersionEvidence.DiffersFromReviewedSource);
        Check(ProviderCatalog.Metadata([samples[3]], true).Rows[0].VersionEvidence == ProviderVersionEvidence.MatchesReviewedSource);
        Check(ProviderCatalog.Metadata([samples[4]], true).Rows[0].VersionEvidence == ProviderVersionEvidence.Unknown);
    }
    private static void VersionReports()
    {
        var metadata = ProviderCatalog.Metadata([new("AutoRetainer", "4.6.2.18", true, false)], true);
        var missing = ProviderQueries.Capture(ProviderKind.AutoRetainer, () => metadata, _ => null);
        Check(missing.Reason == Reason.IpcMissing);
        var failure = ProviderQueries.Capture(ProviderKind.AutoRetainer, () => metadata, _ => throw new InvalidCastException("private secret"));
        Check(failure.Reason == Reason.IpcFailed && failure.Payload is ProviderQueryPayload { Value: null });
        var source = new FakeSource(0);
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.Enqueue(Inspector.Context); engine.Tick();
        var result = engine.View().Results[^1] with { Inspector = Inspector.ProviderReadiness, SchemaVersion = 2, Payload = failure.Payload,
            Outcome = failure.Outcome, Reason = failure.Reason };
        var text = ReportCodec.Describe(result, 0);
        Check(text.Contains("DiffersFromReviewedSource") && text.Contains("4.6.1.26") && !text.Contains("private secret"));
        var preview = ReportCodec.Create(engine.View() with { Results = [result] }, Provenance.Create("0.4.1", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
        using var json = System.Text.Json.JsonDocument.Parse(preview.Content);
        var row = json.RootElement.GetProperty("Results")[0].GetProperty("Payload").GetProperty("Provider");
        Check(row.GetProperty("VersionEvidence").GetString() == "DiffersFromReviewedSource" && row.GetProperty("ReviewedSourceVersion").GetString() == "4.6.1.26");
        Check(Catalog.Get(Inspector.ProviderMetadata).Version == 2 && Catalog.Get(Inspector.ProviderReadiness).Version == 2);
    }
}
