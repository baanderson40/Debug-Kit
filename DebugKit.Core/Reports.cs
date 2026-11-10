using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DebugKit.Core;

public sealed record Provenance(string PluginVersion, string DalamudVersion, string GameBuild, string NativeProfile = "Unsupported",
    string StructsVersion = "Unavailable", string StructsSha256 = "Unavailable")
{
    public static Provenance Create(string? pluginVersion, string? dalamudVersion, string? gameBuild = null, string? nativeProfile = null,
        string? structsVersion = null, string? structsHash = null) =>
        new(SafeVersion(pluginVersion), SafeVersion(dalamudVersion), NativeCompatibility.SafeBuild(gameBuild),
            nativeProfile == "ExperimentalMatched" ? "ExperimentalMatched" : "Unsupported", SafeVersion(structsVersion),
            structsHash is { Length: 64 } && structsHash.All(Uri.IsHexDigit) ? structsHash.ToLowerInvariant() : "Unavailable");

    private static string SafeVersion(string? value) => value is { Length: <= 64 } && Version.TryParse(value, out var v)
        ? v.ToString() : "Unavailable";
}

public sealed record ReportManifest(int FormatVersion, Guid ReportId, DateTimeOffset CreatedUtc,
    Provenance Provenance, long CurrentGeneration, long Evicted, long Dropped, bool CommandAvailable,
    string PrivacyPolicy);
public sealed record DiagnosticReport(ReportManifest Manifest, ImmutableArray<CaptureResult> Results);

// Immutable bytes are used for both preview and export. No recapture between preview and write.
public sealed record ReportPreview(Guid ReportId, ReportFormat Format, ImmutableArray<byte> Bytes)
{
    public string Content => Encoding.UTF8.GetString(Bytes.AsSpan());
    public string FileName => $"debug-kit-{ReportId:N}.{(Format == ReportFormat.Json ? "json" : "txt")}";
}

public static class ReportCodec
{
    private const int MaximumReportBytes = 2 * 1024 * 1024;

    public static int ResultSize(CaptureResult result) => ResultSize(result, null);

    public static int ResultSize(CaptureResult result, PerformanceRecorder? performance)
    {
        using var measurement = performance?.Measure(result.Inspector, PerformanceArea.JsonSerialization) ?? default;
        return JsonSerializer.SerializeToUtf8Bytes(result, ReportJsonContext.Default.CaptureResult).Length;
    }

    public static ReportPreview Create(EngineView view, Provenance provenance, ReportFormat format, DateTimeOffset now,
        PerformanceRecorder? performance = null)
    {
        var inspector = view.Results.Length == 1 ? (Inspector?)view.Results[0].Inspector : null;
        using var preparation = performance?.Measure(inspector, PerformanceArea.PreviewPreparation) ?? default;
        if (!Enum.IsDefined(format) || view.Results.Length > 100) throw new ArgumentOutOfRangeException(nameof(format));
        // Even caller-supplied version strings go through the allowlist; no file paths or arbitrary build strings.
        var safeProvenance = Provenance.Create(provenance.PluginVersion, provenance.DalamudVersion, provenance.GameBuild,
            provenance.NativeProfile, provenance.StructsVersion, provenance.StructsSha256);
        var privacy = view.Results.Any(r => r.Payload is CallbackPayload)
            ? "In-game display names, positions and territory included. " + CallbackText.NumericWarning + " Other inspectors omit raw character/object/owner IDs. No pointer-typed arguments, chat, paths or exception text are captured."
            : "In-game display names (including players/retainers/companions), positions and territory included. No character/object/owner IDs, addresses, paths, chat, or exception text.";
        if (view.Results.Any(r => r.Payload is PluginInventoryPayload)) privacy += " Installed Plugins results disclose plugin inventory.";
        var manifest = new ReportManifest(2, Guid.NewGuid(), now, safeProvenance, view.Generation,
            view.Evicted, view.Dropped, view.CommandAvailable, privacy);
        var report = new DiagnosticReport(manifest, view.Results);
        byte[] payload;
        using (performance?.Measure(inspector, format == ReportFormat.Json ? PerformanceArea.JsonSerialization : PerformanceArea.TextFormatting) ?? default)
            payload = format == ReportFormat.Json ? JsonSerializer.SerializeToUtf8Bytes(report, ReportJsonContext.Default.DiagnosticReport)
                : Encoding.UTF8.GetBytes(ToText(report));
        if (payload.Length > MaximumReportBytes) throw new InvalidOperationException("Report capacity exceeded.");
        return new(manifest.ReportId, format, ImmutableArray.CreateRange(payload));
    }

    public static string Describe(CaptureResult result, long currentGeneration, PerformanceRecorder? performance = null)
    {
        using var measurement = performance?.Measure(result.Inspector, PerformanceArea.TextFormatting) ?? default;
        var text = new StringBuilder();
        text.AppendLine($"{Catalog.Get(result.Inspector).Id} v{result.SchemaVersion} | {result.Outcome} / {result.Reason}");
        text.AppendLine($"Request: {result.RequestId} | generation {result.Generation} ({(result.Generation == currentGeneration ? "same context generation; retained snapshot, not live" : "STALE context")})");
        text.AppendLine($"Requested UTC: {result.RequestedUtc:O}");
        text.AppendLine($"Capture UTC: {result.StartedUtc:O} — {result.CompletedUtc:O}");
        text.AppendLine(FormattableString.Invariant($"Capture span: {result.DurationMs:F2}ms (includes gaps between updates; not CPU time) | steps {result.Steps} | partial {result.Partial} | non-atomic {result.NonAtomic}"));
        if (result.Inspector == Inspector.NearbyObjects)
            text.AppendLine($"Examined {result.Examined} | matched {result.Matched} | retained {result.Objects.Length} | empty {result.Empty} | invalid {result.Invalid} | filtered {result.Filtered} | unreadable {result.Unavailable} | payload-omitted {result.Omitted}");
        else if (result.Inspector is Inspector.AddonList or Inspector.AddonValues or Inspector.AddonNodes or Inspector.ContextMenu or Inspector.Inventory)
            text.AppendLine($"Examined {result.Examined} | payload rows {Payloads.Count(result.Payload)} | empty {result.Empty} | invalid {result.Invalid} | filtered {result.Filtered} | unreadable {result.Unavailable} | payload-omitted {result.Omitted}");
        else if (result.Inspector != Inspector.Context)
            text.AppendLine($"Payload rows {Payloads.Count(result.Payload)} | payload-omitted {result.Omitted}");
        if (result.Payload is SheetPayload or DomainPayload or NetworkPayload)
            text.AppendLine($"Text-shortened fields {Payloads.ShortenedFields(result.Payload)} (separate from omitted rows; source lengths count UTF-16 units)");
        if (DomainPolicies.IsDomain(result.Inspector))
            text.AppendLine($"Captured inputs: {SnapshotLabels.Inputs(result)} | examined {result.Examined} | matched {result.Matched} | empty {result.Empty} | invalid {result.Invalid} | filtered {result.Filtered} | unreadable {result.Unavailable}");
        if (NetworkPolicy.IsNetwork(result.Inspector))
        {
            text.AppendLine($"Captured inputs: {(result.ToolOptions == null ? "unavailable" : SnapshotLabels.Inputs(result))}");
            text.AppendLine($"HTTP status: {result.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable (no response status retained)"}");
        }
        if (result.Inspector == Inspector.AddonCallbacks) text.AppendLine($"Captured inputs: {SnapshotLabels.Inputs(result)}");
        if (result.Inspector is Inspector.JobLookup or Inspector.NpcNameLookup or Inspector.TerritoryLookup or
            Inspector.MovementSpeedTracker or Inspector.TargetDistanceTracker or Inspector.EntityNameWatch or Inspector.InstalledPlugins)
            text.AppendLine($"Captured inputs: {SnapshotLabels.Inputs(result)}");
        if (result.Options is { } options)
            text.AppendLine(FormattableString.Invariant($"Filter: radius {options.Radius:F1}y | kind {options.Kind} | base ID {options.BaseId?.ToString(CultureInfo.InvariantCulture) ?? "any"}"));
        if (result.Context is { } ctx)
        {
            text.AppendLine($"Logged in {ctx.LoggedIn} | player ready {ctx.PlayerAvailable} | territory {Number(ctx.TerritoryId)} | job {Number(ctx.JobId)} | level {ctx.Level?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            text.AppendLine($"Origin/position: {FormatPoint(ctx.Position)}");
            text.AppendLine($"Conditions: combat {ctx.Conditions.InCombat}, casting {ctx.Conditions.Casting}, mounted {ctx.Conditions.Mounted}, occupied {ctx.Conditions.Occupied}, between areas {ctx.Conditions.BetweenAreas}");
        }
        foreach (var row in result.Objects)
            text.AppendLine(FormattableString.Invariant($"{row.Kind} | name {row.Name ?? "unavailable"} | object kind {row.RawKindLabel} ({row.RawKindValue?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}) | base ID {Number(row.BaseId)} | {FormatPoint(row.Position)} | {row.Distance:F2}y | targetable {row.Targetable}"));
        AppendPayload(text, result.Payload);
        if (result.NonAtomic) text.AppendLine("Non-atomic observations across multiple updates: values/slots may change; completeness and identity continuity are not guaranteed.");
        if (result.Partial) text.AppendLine("PARTIAL: limits, cancellation, or unavailable reads prevent a complete capture.");
        return text.ToString();
    }

    private static void AppendPayload(StringBuilder text, ToolPayload? payload)
    {
        switch (payload)
        {
            case CallbackPayload p:
                text.AppendLine($"Native callback observation | scope {CallbackNames.Scope(p.Addon)} | game build {p.GameBuild} | UTC {p.StartedUtc:O} — {p.CompletedUtc:O}");
                text.AppendLine($"Rolling buffer: {p.Rows.Length}/{CallbackBuffer.MaximumEvents} events retained | older events dropped {p.EventsDropped} | read failures {p.ReadFailures}");
                text.AppendLine("PRIVACY: " + CallbackText.NumericWarning);
                foreach (var row in p.Rows)
                {
                    text.AppendLine($"{CallbackNames.Display(p, row)} | {row.ObservedUtc:O} | arguments {row.ArgumentCount} | raw flag {CallbackText.Boolean(row.Flag)} | return {CallbackText.Boolean(row.Returned)} | argument entries omitted {row.ArgumentsOmitted}");
                    foreach (var argument in row.Arguments)
                        text.AppendLine("  " + CallbackText.Argument(argument));
                }
                text.AppendLine("Manual start/stop observation; the rolling buffer retains only the newest bounded events. Omitted entries and withheld values are distinct. String/float/vector/pointer-typed arguments remain excluded; no setup/list-handler/text hooks or callback replay.");
                break;
            case TargetPayload p:
                var r = p.Target.Object;
                text.AppendLine($"Target: {r.Name ?? "unavailable"} | {r.Kind} | {r.RawKindLabel} ({r.RawKindValue}) | base ID {Number(r.BaseId)}");
                text.AppendLine($"Position {FormatPoint(r.Position)} | distance {(r.Distance is { } distance ? distance.ToString("F2", CultureInfo.InvariantCulture) + "y" : "unavailable")} | targetable {r.Targetable}");
                text.AppendLine($"Rotation {OptionalNumber(p.Target.Rotation)} | hitbox {OptionalNumber(p.Target.HitboxRadius)} | HP {Number(p.Target.Hp)}/{Number(p.Target.MaxHp)} | job {Number(p.Target.JobId)} | level {OptionalNumber(p.Target.Level)}");
                text.AppendLine($"Statuses: {p.Target.StatusAvailability} | count {OptionalNumber(p.Target.StatusCount)} | source object IDs withheld{(p.Target.StatusesTruncated ? " | truncated to 80" : "")}");
                foreach (var status in p.Target.Statuses.IsDefault ? ImmutableArray<TargetStatusData>.Empty : p.Target.Statuses)
                    text.AppendLine(status.ReadFailed
                        ? "  status read failed"
                        : $"  status {OptionalNumber(status.StatusId)} | {status.Name ?? "name unavailable"} | remaining {OptionalNumber(status.RemainingTime)}s");
                text.AppendLine("Selected-target observation only; no target changes and no claim of entity continuity across snapshots.");
                break;
            case AddonsPayload p:
                text.AppendLine($"Game build {p.GameBuild}; native profile {p.Profile}. Addon IDs are native UI IDs, not GetAddonByName instance indexes.");
                foreach (var row in p.Rows) text.AppendLine($"{row.Name} | addon ID {row.AddonId} | visible {row.Visible} | ready {row.Ready} | values {row.Values} | nodes {row.Nodes}");
                break;
            case ValuesPayload p:
                text.AppendLine($"{p.Addon} | instance index {p.Instance} | UI lifetime epoch {p.Lifetime} | game build {p.GameBuild}");
                foreach (var row in p.Rows) text.AppendLine($"[{row.Index}] {row.Type}: {row.Value} | decoded {row.Decoded}");
                break;
            case NodesPayload p:
                text.AppendLine($"{p.Addon} | instance index {p.Instance} | UI lifetime epoch {p.Lifetime} | game build {p.GameBuild}");
                foreach (var row in p.Rows) text.AppendLine(FormattableString.Invariant($"list[{row.ListIndex}] node {row.NodeId} | type {row.Type} | depth {row.Depth} | visible {row.Visible} | <{row.X},{row.Y}> {row.Width}x{row.Height} | depth limited {row.DepthLimited}"));
                text.AppendLine("Root ULD list only; component internals/text omitted. No native pointers retained.");
                break;
            case MenuPayload p:
                text.AppendLine($"ContextMenu instance {p.Instance} | UI lifetime epoch {p.Lifetime} | game build {p.GameBuild}");
                foreach (var row in p.Rows) text.AppendLine($"entry[{row.Index}] {row.Value} | decoded {row.Decoded}");
                text.AppendLine("Enabled state unavailable; action mapping unresolved. No menu opening, selection or callback.");
                break;
            case SheetPayload p:
                text.AppendLine($"{p.Sheet} row {p.RowId} | actual sheet language {p.Language} | game build {p.GameBuild}");
                foreach (var row in p.Fields) text.AppendLine($"{row.Field}: {FieldText.Display(row)}");
                break;
            case DiagnosticFieldsPayload p:
                text.AppendLine($"{p.Source} | non-atomic {p.NonAtomic}");
                foreach (var row in p.Fields)
                    text.AppendLine($"{row.Section} | {row.Field} | {row.Type} | {row.Value ?? "(no value)"} | {row.Availability}");
                break;
            case PluginInventoryPayload p:
                text.AppendLine($"Installed plugin inventory | enumeration complete {p.Complete} | {p.Rows.Length} rows. Sharing reveals plugin inventory.");
                foreach (var row in p.Rows) text.AppendLine($"{row.InternalName} | loaded {row.Loaded} | version {row.Version}");
                break;
            case LookupPayload p:
                text.AppendLine($"Typed lookup {p.Kind} | language {p.Language} | source {p.Source} | range complete {p.Complete}");
                foreach (var row in p.Rows)
                {
                    text.AppendLine($"Row {row.RowId}");
                    foreach (var field in row.Fields) text.AppendLine($"  {field.Field}: {FieldText.Display(field)}");
                }
                break;
            case ObservationPayload p:
                text.AppendLine($"Manual observation: {p.Description} | retained {p.Samples.Length}/{ObservationPolicy.Capacity} | older samples dropped {p.Dropped}");
                foreach (var sample in p.Samples)
                {
                    text.AppendLine($"{sample.ObservedUtc:O}");
                    foreach (var field in sample.Fields)
                        text.AppendLine($"  {field.Section} | {field.Field} | {field.Type} | {field.Value ?? "(no value)"} | {field.Availability}");
                }
                text.AppendLine("Observation is bounded and read-only. Names/positions may be present. Entity absence does not prove despawn; name does not prove identity continuity.");
                break;
            case InventoryPayload p:
                text.AppendLine($"Normal player bags only | game build {p.GameBuild}; observed counts are not a guaranteed complete total.");
                foreach (var row in p.Rows) text.AppendLine($"{row.Bag}/slot {row.Slot} | item {row.ItemId} | {row.Name ?? "unavailable"} | quantity {row.Quantity} | HQ {row.Hq}");
                break;
            case ComparisonPayload p:
                text.AppendLine($"Baseline {p.Baseline} → comparison {p.Comparison}");
                text.AppendLine($"Added {p.Added} | removed {p.Removed} | changed {p.Changed} | unchanged {p.Unchanged} | unobserved {p.Unobserved} | ambiguous {p.Ambiguous} | indeterminate {p.Indeterminate} | complete evidence {p.CompleteEvidence}");
                foreach (var row in p.Rows) text.AppendLine($"{row.Change} | {row.Key} | before: {row.Before ?? "unavailable/ambiguous"}{(row.BeforeShortened ? " [text shortened]" : "")} | after: {row.After ?? "unavailable/ambiguous"}{(row.AfterShortened ? " [text shortened]" : "")}");
                text.AppendLine("Unobserved means missing from one observation, not proof of removal. Object/target identity continuity is not established by name/base ID.");
                text.AppendLine("Indeterminate means source field text was shortened; matching prefixes cannot establish unchanged full text. Diff summaries may also be shortened.");
                break;
            case ProviderPayload p:
                text.AppendLine($"Evidence: {p.Source} | metadata enumeration complete {p.EnumerationComplete}");
                foreach (var row in p.Rows)
                    text.AppendLine($"{row.Provider} | installed {row.Installed} | loaded {row.Loaded} | version {row.Version ?? "unknown"} | development {row.DevelopmentBuild} | contract {row.Contract} | reviewed source {row.ReviewedSourceVersion ?? "unknown"} | version evidence {row.VersionEvidence} | endpoint {row.EndpointPresent} | responding {row.Responding} | ready {row.Ready?.ToString() ?? "unknown"}");
                text.AppendLine("Fixed catalog only. Unknown is not false; metadata does not establish endpoint presence or readiness. Provider comparisons unsupported.");
                text.AppendLine("Version/dev metadata is advisory, not an invocation gate or proof of installed code semantics. ReviewedEndpoint identifies a fixed source-reviewed getter only.");
                break;
            case ProviderQueryPayload p:
                text.AppendLine($"Public typed IPC | {p.Provider.Provider} {p.Provider.Version} | contract {p.Provider.Contract} | endpoint {p.Endpoint}");
                text.AppendLine($"Reviewed source version {p.Provider.ReviewedSourceVersion ?? "unknown"} | version evidence {p.Provider.VersionEvidence} | development {p.Provider.DevelopmentBuild} (advisory; not gated)");
                text.AppendLine($"Endpoint present {p.Provider.EndpointPresent} | responding {p.Provider.Responding} | {p.Query}: {p.Value?.ToString() ?? "unknown"} | ready {p.Provider.Ready?.ToString() ?? "unknown"}");
                text.AppendLine("One explicit boolean observation, not provider control or a readiness guarantee. Busy/suppressed do not imply ready. Same-version reload continuity cannot be proven.");
                text.AppendLine("A successful typed response establishes only that this call returned bool, not that installed provider behavior matches reviewed source.");
                break;
            case NetworkPayload p:
                text.AppendLine($"External source: {p.Service} | retrieved UTC {p.RetrievedUtc:O} | no local cache or automatic refresh");
                text.AppendLine("Source timestamps/ages are historical evidence, not current availability or a freshness guarantee. Missing/future timestamps have unknown age. Network comparisons unsupported.");
                foreach (var row in p.Rows)
                {
                    text.AppendLine($"{row.Key} | {row.Name}");
                    foreach (var field in row.Fields) text.AppendLine($"  {field.Field}: {FieldText.Display(field)}");
                }
                break;
            case DomainPayload p:
                var territory = p.Territory is { } t ? Number(t) : DomainPolicies.IsStatic(p.Kind) || p.Kind == Inspector.Currency ? "not applicable" : "unavailable";
                var lifetime = p.Lifetime is { } epoch ? OptionalNumber((long?)epoch) : p.Kind is Inspector.FamiliarUi or Inspector.HousingUi ? "unavailable" : "not applicable";
                text.AppendLine($"Evidence: {p.Source} | language {p.Language} | game build {p.GameBuild} | territory {territory} | lifetime {lifetime}");
                if (p.FilterOrigin != null) text.AppendLine($"Spatial filter origin: {FormatPoint(p.FilterOrigin)} (capture-start; comparisons require matching origin)");
                text.AppendLine($"Limitations: {p.Limitations}");
                foreach (var row in p.Rows)
                {
                    text.AppendLine($"{row.Key} | {row.Name}");
                    foreach (var field in row.Fields) text.AppendLine($"  {field.Field}: {FieldText.Display(field)}");
                }
                break;
        }
    }

    private static string ToText(DiagnosticReport report)
    {
        var m = report.Manifest;
        var text = new StringBuilder();
        text.AppendLine($"Debug Kit report format {m.FormatVersion} | {m.ReportId} | UTC {m.CreatedUtc:O}");
        text.AppendLine($"Plugin {m.Provenance.PluginVersion} | Dalamud {m.Provenance.DalamudVersion} | game build {m.Provenance.GameBuild} | native profile {m.Provenance.NativeProfile}");
        text.AppendLine($"FFXIVClientStructs {m.Provenance.StructsVersion} | SHA-256 {m.Provenance.StructsSha256}");
        text.AppendLine($"Evicted {m.Evicted} | dropped {m.Dropped} | command owned {m.CommandAvailable}");
        text.AppendLine(m.PrivacyPolicy);
        foreach (var result in report.Results)
        {
            text.AppendLine();
            text.Append(Describe(result, m.CurrentGeneration));
        }
        return text.ToString();
    }

    private static string Number(uint? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
    private static string OptionalNumber<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture) ?? "unavailable";
    private static string FormatPoint(Point? point) => point is { } p
        ? FormattableString.Invariant($"<{p.X:F2}, {p.Y:F2}, {p.Z:F2}>") : "unavailable";
}

public enum ExportStatus { Success, Cancelled, AlreadyExists, Failed, Busy, Disposed }

/// <summary>Only writes explicit previews to a fixed plugin-local directory. Never overwrites reports.</summary>
public sealed class ReportExporter : IDisposable
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy, disposed;

    public ReportExporter(string directory) => this.directory = Path.GetFullPath(directory);

    // Local UI navigation only; never included in reports.
    public string DirectoryPath => directory;

    public async Task<ExportStatus> ExportAsync(ReportPreview preview, CancellationToken cancellation = default)
    {
        CancellationTokenSource linked;
        lock (gate)
        {
            if (disposed) return ExportStatus.Disposed;
            if (busy) return ExportStatus.Busy;
            busy = true;
            linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation);
        }
        // A unique temporary filename is always confined to the fixed directory.
        var temporary = Path.Combine(directory, $".debug-kit-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            if (preview.Bytes.Length > 2 * 1024 * 1024 || !Enum.IsDefined(preview.Format)) return ExportStatus.Failed;
            linked.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var final = Path.Combine(directory, preview.FileName);
            if (File.Exists(final)) return ExportStatus.AlreadyExists;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous))
            {
                created = true;
                await stream.WriteAsync(preview.Bytes.AsMemory(), linked.Token).ConfigureAwait(false);
                await stream.FlushAsync(linked.Token).ConfigureAwait(false);
            }
            // Serialize finalization with disposal. Disposal cannot race a late success into the UI.
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                try { File.Move(temporary, final, overwrite: false); }
                catch (IOException) when (File.Exists(final)) { return ExportStatus.AlreadyExists; }
            }
            created = false;
            return ExportStatus.Success;
        }
        catch (OperationCanceledException) { return ExportStatus.Cancelled; }
        catch (Exception) { return ExportStatus.Failed; }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (Exception) { /* Only this export's own temp file; never broaden cleanup. */ }
            }
            linked.Dispose();
            lock (gate)
            {
                busy = false;
                if (disposed) lifetime.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            if (!busy) lifetime.Dispose();
        }
    }
}
