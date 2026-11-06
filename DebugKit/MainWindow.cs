using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DebugKit.Core;

namespace DebugKit;

internal sealed class MainWindow : Window, IDisposable
{
    private readonly DiagnosticEngine engine;
    private readonly ReportExporter exporter;
    private readonly Provenance provenance;
    private readonly Configuration configuration;
    private readonly Action savePreferences;
    private readonly CancellationTokenSource lifetime = new();
    private Inspector selected;
    private float radius;
    private int kind;
    private string search = "", baseId = "", status = "Explicit captures only. Nothing controls the game.";
    private Guid? selectedResult, renderedResult;
    private long renderedGeneration = -1;
    private string resultText = "", previewText = "";
    private ReportPreview? preview;
    private Task<ExportStatus>? exportTask;
    private CancellationTokenSource? exportCancellation;
    private bool disposed, detailsExpanded;
    private ToolOptions toolOptions = new();
    private readonly Dictionary<Inspector, ToolOptions> forms = new();
    private string sheetRow = "1", itemFilter = "", nodeFilter = "";
    private string domainId = "", stateFilter = "", scheduleEpoch = "", remainingSeconds = "";
    private bool toolInputValid = true;
    private bool nativeReadsEnabled = true;
    private long profilingRefresh;
    private ImmutableArray<PerformanceSummary> profilingRows = [];
    private string profilingText = "";
    private static readonly string[] Kinds = Enum.GetNames<ObjectCategory>();

    public MainWindow(DiagnosticEngine engine, ReportExporter exporter, Provenance provenance,
        Configuration configuration, Action savePreferences) : base("Debug Kit###DebugKitMain")
    {
        this.engine = engine;
        this.exporter = exporter;
        this.provenance = provenance;
        this.configuration = configuration;
        this.savePreferences = savePreferences;
        radius = configuration.Radius;
        Size = new Vector2(1060, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(700, 480), MaximumSize = new(float.MaxValue, float.MaxValue) };
    }

    public override void Draw()
    {
        if (disposed) return;
        using var measurement = engine.Performance.Measure(null, PerformanceArea.WindowDraw);
        DrawCore();
    }

    private void DrawCore()
    {
        ObserveExport();
        var view = engine.View();
        if (!ImGui.BeginTable("navigation-tool-layout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV)) return;
        try
        {
            ImGui.TableSetupColumn("Navigation", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Tool", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var show = ImGui.BeginChild("navigation", new Vector2(0, 0), true,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            try { if (show) DrawNavigation(); }
            finally { ImGui.EndChild(); }
            ImGui.TableNextColumn();
            show = ImGui.BeginChild("selected-tool", new Vector2(0, 0), true);
            try { if (show) DrawTool(view); }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.EndTable(); }
    }

    // Search and scrolling tool links above a pinned plugin-details footer. Tool controls live on the right.
    private void DrawNavigation()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##find-tool", "Find tool", ref search, 80);
        ImGui.Spacing();
        var details = $"Debug Kit {provenance.PluginVersion}\nDalamud {provenance.DalamudVersion}\nGame {provenance.GameBuild} (installation stamp)\nFFXIVClientStructs {provenance.StructsVersion}\nNative profile: {provenance.NativeProfile}\nNative reads experimental; session-enabled by default, exact compatibility required. Matching is not a memory-safety certification. Callback observer uses manual Start/Stop; integers may contain IDs. No replay or provider controls.";
        var available = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var footerHeight = ImGui.GetFrameHeight() + spacing * 3;
        if (detailsExpanded)
            footerHeight += ImGui.CalcTextSize(details, false, MathF.Max(1, available.X)).Y + spacing;
        // Bound the expanded footer in short windows; it scrolls independently without displacing navigation.
        footerHeight = MathF.Min(footerHeight, MathF.Max(ImGui.GetFrameHeight(), available.Y * 0.5f));
        var show = ImGui.BeginChild("tool-navigation-list", new Vector2(0, MathF.Max(1, available.Y - footerHeight)), false);
        try
        {
            if (show)
            {
                foreach (var section in Catalog.All
                    .OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                    .GroupBy(d => d.Category))
                {
                    var tools = section.Where(d => search.Length == 0 ||
                        (d.Title + d.Category + d.Id).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (tools.Length == 0) continue;
                    ImGui.TextDisabled(section.Key);
                    ImGui.Separator();
                    foreach (var descriptor in tools)
                    {
                        if (ImGui.Selectable(descriptor.Title, selected == descriptor.Inspector) && selected != descriptor.Inspector)
                        {
                            Navigate(descriptor.Inspector);
                        }
                    }
                    ImGui.Spacing();
                }
            }
        }
        finally { ImGui.EndChild(); }
        ImGui.Separator();
        show = ImGui.BeginChild("navigation-details-footer", new Vector2(0, 0), false);
        try
        {
            if (show)
            {
                detailsExpanded = ImGui.CollapsingHeader("Debug Kit details");
                if (detailsExpanded) ImGui.TextWrapped(details);
            }
        }
        finally { ImGui.EndChild(); }
    }

    private void DrawTool(EngineView view)
    {
        var descriptor = Catalog.Get(selected);
        ImGui.TextUnformatted(descriptor.Title);
        ImGui.TextDisabled($"{descriptor.Id} v{descriptor.Version} | READ-ONLY");
        ImGui.TextWrapped(descriptor.Help);
        ImGui.TextWrapped("Reports include in-game names (including players/retainers/companions), positions and territory. Consented numeric callback logs may include IDs; other tools omit raw identifiers. Review before sharing.");
        if (!view.CommandAvailable)
            ImGui.TextWrapped("/debugkit is already owned by another handler. Open this window through Dalamud's plugin UI.");
        ImGui.Separator();
        DrawInputs();
        ImGui.Separator();
        ImGui.TextWrapped($"Queued {view.Pending}/16 | active {(view.ActiveRequest != null ? "yes" : "no")} | examined {view.Examined} | evicted {view.Evicted} | dropped {view.Dropped} | rejected {view.Rejected}");
        ImGui.TextWrapped(status);
        DrawPerformance();
        if (exportTask != null && preview == null && ImGui.Button("Cancel export")) exportCancellation?.Cancel();
        ImGui.Separator();
        DrawResults(view);
    }

    private void DrawPerformance()
    {
        if (!ImGui.CollapsingHeader("Performance profiling")) return;
        var enabled = engine.Performance.Enabled;
        if (ImGui.Checkbox("Collect timings and managed allocations (session)", ref enabled))
        {
            engine.Performance.Enabled = enabled;
            profilingRefresh = 0;
            renderedResult = null;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear profiling samples"))
        {
            engine.Performance.Clear(); profilingRows = []; profilingRefresh = 0;
        }
        ImGui.TextWrapped("Off by default; no automatic captures. Wall time, not CPU time. Nested measurements overlap; do not add them. First = first measured operation since reset, not necessarily a cold game-data load. Warm = latest 256 later operations.");
        ImGui.TextWrapped("Capture span includes gaps between updates; FrameworkStep measures synchronous work including context reads, retention and serialization. ReaderJob is one tool step (or one nearby object getter); ContextRead is separate. Allocations are current-thread managed bytes, not native memory, GC pause time or total retained memory. 2ms is cooperative, not guaranteed.");
        if (profilingRefresh == 0 || Stopwatch.GetElapsedTime(profilingRefresh).TotalSeconds >= 1)
        {
            profilingRows = engine.Performance.Snapshot();
            profilingText = PerformanceRecorder.Describe(profilingRows);
            profilingRefresh = Stopwatch.GetTimestamp();
        }
        ImGui.BeginDisabled(profilingRows.IsEmpty);
        try
        {
            if (ImGui.Button("Copy profiling summary"))
                ImGui.SetClipboardText(profilingText);
        }
        finally { ImGui.EndDisabled(); }
        var show = ImGui.BeginChild("profiling-statistics", new Vector2(0, 180), true);
        try
        {
            if (show) ImGui.TextUnformatted(profilingText);
        }
        finally { ImGui.EndChild(); }
    }

    private void DrawInputs()
    {
        if (selected == Inspector.NearbyObjects)
        {
            ImGui.SetNextItemWidth(130);
            ImGui.InputFloat("Radius (1–200y)", ref radius, 5, 20, "%.1f");
            ImGui.SetNextItemWidth(140);
            ImGui.Combo("Kind", ref kind, Kinds, Kinds.Length);
            ImGui.InputText("Base ID (optional)", ref baseId, 10);
            ImGui.TextWrapped("Maximum 128 rows / 1,024 table entries. Positions use capture-start origin. In-game display names are included for all kinds.");
        }
        toolInputValid = true;
        if (selected is not (Inspector.Context or Inspector.NearbyObjects)) DrawToolInputs();
        var valid = TryOptions(out var options) && toolInputValid;
        if (selected is Inspector.MovementSpeedTracker or Inspector.TargetDistanceTracker or Inspector.EntityNameWatch)
        {
            if (engine.ActiveObservation == selected)
            {
                if (ImGui.Button("Stop observation")) status = engine.StopObservation()
                    ? "Stopping observation; bounded samples will be retained." : "No active observation to stop.";
            }
            else
            {
                ImGui.BeginDisabled(!valid || !engine.CanStartObservation);
                try { if (ImGui.Button("Start observation")) Queue(selected, options); }
                finally { ImGui.EndDisabled(); }
                if (!engine.CanStartObservation) ImGui.TextDisabled("Wait for other diagnostic work to finish before starting an observation.");
            }
        }
        else if (selected == Inspector.AddonCallbacks)
        {
            if (engine.HasCallbackCapture)
            {
                if (ImGui.Button("Stop capture")) status = engine.StopCallbackCapture()
                    ? "Stopping callback observation; retained rows will be saved."
                    : "No active callback observation to stop.";
            }
            else
            {
                ImGui.BeginDisabled(!valid || !engine.CanStartCallbackCapture);
                try { if (ImGui.Button("Start capture")) Queue(selected, options); }
                finally { ImGui.EndDisabled(); }
                if (!engine.CanStartCallbackCapture) ImGui.TextDisabled("Wait for other diagnostic work to finish before starting callback observation.");
            }
        }
        else
        {
            ImGui.BeginDisabled(!valid);
            try { if (ImGui.Button("Capture")) Queue(selected, options); }
            finally { ImGui.EndDisabled(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                engine.Cancel();
                status = "Only Debug Kit capture requests cancelled.";
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Cancel only Debug Kit's active and queued diagnostic requests.");
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear results"))
        {
            engine.ClearResults(); selectedResult = null; preview = null; previewText = "";
            status = "Local history cleared. Existing requests continue unless cancelled.";
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear Debug Kit's local result history. Does not cancel pending work.");
        if (selected == Inspector.NearbyObjects)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(!valid);
            try
            {
                if (ImGui.Button("Save radius"))
                {
                    configuration.Radius = radius;
                    try { savePreferences(); status = "Saved Debug Kit radius preference only."; }
                    catch (Exception) { status = "Could not save Debug Kit preference. No error payload retained."; }
                }
            }
            finally { ImGui.EndDisabled(); }
        }
        if (!valid) ImGui.TextWrapped(selected == Inspector.NearbyObjects
            ? "Radius must be finite, 1–200. Base ID must be blank or a nonzero uint."
            : "Check the selected tool's inputs/ranges. Comparisons require two different retained snapshots.");
    }

    private bool TryOptions(out CaptureOptions? options)
    {
        options = null;
        if (selected != Inspector.NearbyObjects) return true;
        uint? id = null;
        if (!string.IsNullOrWhiteSpace(baseId))
        {
            if (!uint.TryParse(baseId, out var parsed) || parsed == 0) return false;
            id = parsed;
        }
        options = new(radius, (ObjectCategory)kind, id);
        return options.IsValid;
    }

    private void Queue(Inspector inspector, CaptureOptions? options)
    {
        var result = inspector is Inspector.Context or Inspector.NearbyObjects
            ? engine.Enqueue(inspector, options) : engine.EnqueueTool(inspector,
                toolOptions with { ExperimentalNative = nativeReadsEnabled && provenance.NativeProfile == "ExperimentalMatched" });
        status = $"Request: {result}.";
        if (NetworkPolicy.IsNetwork(inspector)) toolOptions = toolOptions with { NetworkConsent = false };
    }

    private void Navigate(Inspector inspector, ToolOptions? prefill = null)
    {
        forms[selected] = toolOptions with { ExperimentalNative = false, NetworkConsent = false };
        selected = inspector;
        toolOptions = prefill ?? forms.GetValueOrDefault(inspector) ?? new(Addon: inspector == Inspector.AddonList ? "" : "ContextMenu",
            Sheet: inspector == Inspector.FamiliarData ? SheetKind.XBMPet : SheetKind.Item, ItemId: inspector == Inspector.Currency ? 1u : null);
        if (inspector == Inspector.AddonCallbacks && !forms.ContainsKey(inspector) && prefill == null) toolOptions = toolOptions with { Addon = "" };
        sheetRow = toolOptions.RowId.ToString(); itemFilter = toolOptions.ItemId?.ToString() ?? "";
        nodeFilter = toolOptions.NodeId?.ToString() ?? "";
        domainId = toolOptions.FilterId?.ToString() ?? ""; stateFilter = toolOptions.StateFilter?.ToString() ?? "";
        scheduleEpoch = toolOptions.ScheduleEpoch?.ToString("O") ?? "";
        remainingSeconds = toolOptions.RemainingSeconds?.ToString() ?? "";
        toolOptions = toolOptions with { NetworkConsent = false };
        selectedResult = null; preview = null; previewText = "";
    }

    private void DrawToolInputs()
    {
        if (selected == Inspector.AddonCallbacks)
        {
            var addon = toolOptions.Addon;
            ImGui.BeginDisabled(engine.HasCallbackCapture);
            ImGui.InputTextWithHint("Addon filter (optional)", "Blank = all addons", ref addon, 31);
            ImGui.EndDisabled();
            toolInputValid &= addon.Length == 0 || CallbackNames.Clean(addon) == addon;
            ImGui.TextWrapped("Blank filter captures all addons; optional name limits to one exact addon. Press Start capture, then Stop capture when done. Newest 64 events stay in a rolling buffer; older rows are dropped and counted. Stopping preserves the log. Context changes and unload cancel it.");
            ImGui.TextWrapped("Integers are captured without redaction and may contain IDs. This observer changes executable routing; exact compatibility is not a safety certification. Other plugins' hooks can interact with it.");
            ImGui.TextWrapped(CallbackText.NumericWarning);
            toolOptions = toolOptions with { Addon = addon };
        }
        if (NetworkPolicy.IsNetwork(selected))
        {
            var world = (int)toolOptions.WorldId;
            if (selected == Inspector.MarketSummary)
            {
                toolOptions = toolOptions with { MarketScope = EnumInput("Scope", toolOptions.MarketScope) };
                if (toolOptions.MarketScope == MarketScope.DataCenter)
                    toolOptions = toolOptions with { DataCenter = EnumInput("Data center", toolOptions.DataCenter) };
                ImGui.InputText("Item ID", ref itemFilter, 10);
                toolInputValid &= uint.TryParse(itemFilter, out var item) && item > 0;
                toolOptions = toolOptions with { ItemId = item };
            }
            if (selected == Inspector.HousingService || toolOptions.MarketScope == MarketScope.World)
                ImGui.InputInt("Public world ID (user supplied)", ref world);
            if (selected == Inspector.HousingService)
                toolOptions = toolOptions with { District = EnumInput("District", toolOptions.District) };
            var consent = toolOptions.NetworkConsent;
            ImGui.TextWrapped(selected == Inspector.MarketSummary
                ? "Capture sends one HTTPS GET to universalis.app: supplied item and public world/DC only. No character identity or report upload."
                : "Capture sends one HTTPS GET to paissadb.zhu.codes: supplied public world/district only. No login, character identity, location or report upload.");
            ImGui.TextWrapped("Service sees your IP address. Explicit requests only; no redirects/retries/local cache. 10s timeout, 128KiB body cap, one request/10s globally. Cancel affects only Debug Kit. Review inputs before Capture.");
            ImGui.Checkbox("Allow external request for this capture", ref consent);
            toolOptions = toolOptions with { WorldId = world > 0 ? (uint)world : 0, NetworkConsent = consent };
        }
        if (selected == Inspector.ProviderMetadata)
            ImGui.TextWrapped("Capture fixed provider metadata once. No IPC calls. Incomplete enumeration preserves unknown installation states; loaded is not ready.");
        if (selected == Inspector.ProviderReadiness)
        {
            toolOptions = toolOptions with { Provider = EnumInput("Provider", toolOptions.Provider) };
            var contract = ProviderCatalog.Contracts.FirstOrDefault(x => x.Provider == toolOptions.Provider);
            ImGui.TextWrapped(contract == null ? "No audited query for this provider; metadata only."
                : $"Reviewed endpoint: {contract.Endpoint} → bool; source version {contract.ReviewedSourceVersion} (reference only). Returned field: {contract.Query}.");
            ImGui.TextWrapped("Version differences/dev flags do not block. Only fixed source-reviewed endpoints; missing or incompatible responses stay unavailable. Installed implementation semantics are unverified. No timers/offline character roster access.");
        }
        if (DomainPolicies.IsDomain(selected)) DrawDomainInputs();
        if (selected == Inspector.AddonList)
        {
            toolOptions = toolOptions with { Scope = EnumInput("Scope", toolOptions.Scope) };
            var filter = toolOptions.Addon;
            ImGui.InputText("Addon name filter", ref filter, 64);
            toolOptions = toolOptions with { Addon = filter };
        }
        if (selected is Inspector.AddonValues or Inspector.AddonNodes)
        {
            var addon = toolOptions.Addon;
            ImGui.InputText("Addon name", ref addon, 64);
            toolOptions = toolOptions with { Addon = addon };
        }
        if (selected is Inspector.AddonValues or Inspector.AddonNodes or Inspector.ContextMenu)
        {
            var instance = toolOptions.Instance;
            ImGui.InputInt("Instance index (1–8)", ref instance);
            toolOptions = toolOptions with { Instance = instance };
        }
        if (selected == Inspector.AddonValues)
        {
            var start = toolOptions.Start; var count = toolOptions.Count;
            ImGui.InputInt("Start value index", ref start); ImGui.InputInt("Count (1–128)", ref count);
            toolOptions = toolOptions with { Start = start, Count = count };
            ImGui.TextWrapped("Bool and reviewed ContextMenu count fields decoded. Unclassified numbers/strings/pointers omitted to avoid leaking identity/address data.");
        }
        if (selected == Inspector.AddonNodes)
        {
            var depth = toolOptions.Depth;
            ImGui.InputInt("Max parent depth (1–8)", ref depth);
            ImGui.InputText("Node ID (optional)", ref nodeFilter, 10);
            toolInputValid &= OptionalId(nodeFilter, out var nodeId);
            toolOptions = toolOptions with { Depth = depth, NodeId = nodeId };
            ImGui.TextWrapped("Up to 256 root ULD list entries; no component internals or arbitrary text. A single bounded step, not a persistent pointer traversal.");
        }
        if (selected == Inspector.SheetRow)
        {
            toolOptions = toolOptions with { Sheet = EnumInput("Sheet", toolOptions.Sheet), Language = EnumInput("Language", toolOptions.Language) };
            ImGui.InputText("Row ID", ref sheetRow, 10);
            toolInputValid &= uint.TryParse(sheetRow, out var row);
            toolOptions = toolOptions with { RowId = row };
            ImGui.TextWrapped("Explicit supported fields only. Referenced row IDs are not automatically followed. Missing rows/languages remain unavailable.");
        }
        if (selected is Inspector.JobLookup or Inspector.NpcNameLookup or Inspector.TerritoryLookup)
        {
            if (selected == Inspector.JobLookup)
            {
                var listJobs = toolOptions.ListJobs;
                ImGui.Checkbox("List bounded ClassJob ID range (1–128)", ref listJobs);
                toolOptions = toolOptions with { ListJobs = listJobs };
            }
            if (selected == Inspector.NpcNameLookup)
            {
                var npcSheets = new[] { SheetKind.BNpcName, SheetKind.ENpcResident };
                var sheetIndex = Math.Max(0, Array.IndexOf(npcSheets, toolOptions.Sheet));
                ImGui.Combo("NPC source sheet", ref sheetIndex, npcSheets.Select(x => x.ToString()).ToArray(), npcSheets.Length);
                toolOptions = toolOptions with { Sheet = npcSheets[sheetIndex] };
            }
            toolOptions = toolOptions with { Language = EnumInput("Language", toolOptions.Language) };
            if (selected != Inspector.JobLookup || !toolOptions.ListJobs)
            {
                ImGui.InputText("Row ID", ref sheetRow, 10);
                toolInputValid &= uint.TryParse(sheetRow, out var lookupRow) && lookupRow > 0;
                toolOptions = toolOptions with { RowId = lookupRow };
            }
            ImGui.TextWrapped(selected switch
            {
                Inspector.JobLookup => "ClassJob row: localized name and abbreviation. IDs are not hard-coded.",
                Inspector.NpcNameLookup => "Direct typed BNpcName or ENpcResident lookup only. No inferred cross-sheet relationship.",
                _ => "TerritoryType row with explicit PlaceName and Map references. No generic reference traversal.",
            });
        }
        if (selected == Inspector.BigDump)
            ImGui.TextWrapped("One explicit, allowlisted public API snapshot. Unsupported target slots remain labeled; no object IDs, addresses, reflection, or recursive dumps.");
        if (selected == Inspector.ConditionFlags)
            ImGui.TextWrapped("Current installed SDK ConditionFlag names/values. Snapshot only; no chat messages or automated change tracking.");
        if (selected == Inspector.PlayerStatuses)
            ImGui.TextWrapped("Status-list fields are reported only when supported by installed SDK. Source object IDs are withheld.");
        if (selected == Inspector.InstalledPlugins)
            ImGui.TextWrapped("Public internal name, load state, and sanitized version only. No IPC calls/configuration reads. Copy/export reveals installed plugin inventory.");
        if (selected is Inspector.MovementSpeedTracker or Inspector.TargetDistanceTracker or Inspector.EntityNameWatch)
        {
            var interval = toolOptions.ObservationInterval;
            ImGui.InputFloat("Sampling interval (seconds)", ref interval, 0.1f, 0.5f, "%.2f");
            toolOptions = toolOptions with { ObservationInterval = interval };
            if (selected == Inspector.MovementSpeedTracker)
            {
                var minimum = toolOptions.MinimumDistance; var rolling = toolOptions.RollingSamples; var movingOnly = toolOptions.OnlyWhenMoving;
                ImGui.InputFloat("Minimum flat displacement (y)", ref minimum, 0.1f, 1, "%.2f");
                ImGui.InputInt("Rolling average samples (1–30)", ref rolling);
                ImGui.Checkbox("Average only moving samples", ref movingOnly);
                toolOptions = toolOptions with { MinimumDistance = minimum, RollingSamples = rolling, OnlyWhenMoving = movingOnly };
                ImGui.TextWrapped("Samples are displacement/time, not authoritative movement speed. Displacements over 100y are marked as discontinuities and excluded from the rolling average.");
            }
            if (selected == Inspector.EntityNameWatch)
            {
                var name = toolOptions.ObservationName; var exact = toolOptions.ExactName;
                ImGui.InputText("Entity name", ref name, 64);
                ImGui.Checkbox("Exact match (off = substring)", ref exact);
                toolOptions = toolOptions with { ObservationName = name, ExactName = exact };
                ImGui.TextWrapped("Each scan examines at most 1,024 table entries and retains at most 16 candidates. No match does not prove despawn; no map flags are created.");
            }
            ImGui.TextWrapped("Manual Start/Stop only. One observation at a time; navigating away does not stop it. Context changes cancel it. Newest 128 samples retained; dropped count is reported.");
        }
        if (selected == Inspector.Inventory)
        {
            toolOptions = toolOptions with { Bag = EnumInput("Bag", toolOptions.Bag), Quality = EnumInput("Quality", toolOptions.Quality) };
            ImGui.InputText("Item ID (optional)", ref itemFilter, 10);
            toolInputValid &= OptionalId(itemFilter, out var item);
            var includeEmpty = toolOptions.IncludeEmpty;
            ImGui.Checkbox("Include empty slots", ref includeEmpty);
            toolOptions = toolOptions with { ItemId = item, IncludeEmpty = includeEmpty };
            ImGui.TextWrapped("Normal bags only, no retainer/saddlebag data. Unloaded containers are unavailable, not empty. Captures spanning updates are non-atomic.");
        }
        if (selected == Inspector.Compare)
        {
            var snapshots = engine.View().Results.Where(r => r.Inspector != Inspector.Compare).ToArray();
            toolOptions = toolOptions with
            {
                Baseline = SnapshotInput("Baseline", toolOptions.Baseline, snapshots),
                Comparison = SnapshotInput("Comparison", toolOptions.Comparison, snapshots),
            };
            var changes = toolOptions.ChangesOnly;
            ImGui.Checkbox("Changed/added/removed/uncertain only", ref changes);
            toolOptions = toolOptions with { ChangesOnly = changes };
            ImGui.TextWrapped("Requires matching tool/schema/inputs and compatible context/build/lifetime. Name/base ID never proves entity identity. Partial/multi-update captures cannot prove removals.");
        }
        if (selected is Inspector.AddonList or Inspector.AddonValues or Inspector.AddonNodes or Inspector.ContextMenu or Inspector.Inventory or Inspector.AddonCallbacks || DomainPolicies.NeedsNative(selected))
        {
            ImGui.TextWrapped($"Native profile: {provenance.NativeProfile}; game {provenance.GameBuild}. Exact-match experimental reads—not a memory-safety certification. Session authorization defaults enabled; no capture runs automatically.");
            ImGui.BeginDisabled(provenance.NativeProfile != "ExperimentalMatched");
            try { ImGui.Checkbox("Enable experimental native reads (session)", ref nativeReadsEnabled); }
            finally { ImGui.EndDisabled(); }
        }
        toolInputValid &= toolOptions.IsValid(selected);
    }

    private void DrawDomainInputs()
    {
        if (selected is Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents or Inspector.Casts or Inspector.WorldPlacements or Inspector.CollisionBounds)
        {
            var radius = toolOptions.DomainRadius;
            ImGui.InputFloat("Radius (1–200y)", ref radius, 5, 20, "%.1f");
            toolOptions = toolOptions with { DomainRadius = radius };
        }
        if (selected is Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents or Inspector.Casts)
        {
            ImGui.InputText(selected == Inspector.Casts ? "Action ID (optional)" : "FATE row ID (optional)", ref domainId, 10);
            toolInputValid &= OptionalId(domainId, out var id);
            var name = toolOptions.NameFilter;
            ImGui.InputText("Name filter", ref name, 64);
            toolOptions = toolOptions with { FilterId = id, NameFilter = name };
            if (selected == Inspector.Casts)
            {
                var only = toolOptions.SelectedOnly;
                ImGui.Checkbox("Selected target only", ref only);
                toolOptions = toolOptions with { SelectedOnly = only };
            }
            else
            {
                ImGui.InputText("Numeric FATE state (optional, 0–255)", ref stateFilter, 3);
                int? state = null;
                if (stateFilter.Length > 0)
                {
                    if (int.TryParse(stateFilter, out var value)) state = value;
                    else toolInputValid = false;
                }
                toolOptions = toolOptions with { StateFilter = state };
            }
            ImGui.TextWrapped("Up to 32 examined entries/step, 1,024 examined entries and 128 retained rows. Multi-step results are non-atomic. No CE runtime state inferred.");
        }
        if (selected is Inspector.WorldPlacements or Inspector.CollisionBounds)
        {
            toolOptions = toolOptions with { Asset = EnumInput("Installed LGB", toolOptions.Asset) };
            if (selected == Inspector.WorldPlacements)
            {
                toolOptions = toolOptions with { Placement = EnumInput("Placement type", toolOptions.Placement) };
                ImGui.InputText("Base ID (optional)", ref domainId, 10);
                toolInputValid &= OptionalId(domainId, out var id);
                toolOptions = toolOptions with { FilterId = id };
            }
            ImGui.TextWrapped("Static installed placements, not runtime instances. Up to 256 retained rows/1,024 examined entries. Radius filters placement origin distance, not bounds overlap. Raw collision transform/shape is not a proven AABB. Initial asset load is not preemptible.");
        }
        if (selected is Inspector.FamiliarData or Inspector.Duty or Inspector.RetainerTimers)
        {
            if (selected == Inspector.FamiliarData)
            {
                var choices = new[] { SheetKind.XBMPet, SheetKind.XBMItem, SheetKind.XBMContent };
                var index = Math.Max(0, Array.IndexOf(choices, toolOptions.Sheet));
                ImGui.Combo("Familiar sheet", ref index, choices.Select(x => x.ToString()).ToArray(), choices.Length);
                toolOptions = toolOptions with { Sheet = choices[index] };
            }
            if (selected == Inspector.Duty) toolOptions = toolOptions with { Sheet = SheetKind.ContentFinderCondition };
            if (selected == Inspector.RetainerTimers) toolOptions = toolOptions with { Sheet = SheetKind.RetainerTask };
            toolOptions = toolOptions with { Language = EnumInput("Language", toolOptions.Language) };
            ImGui.InputText("Row ID", ref sheetRow, 10);
            toolInputValid &= uint.TryParse(sheetRow, out var row);
            toolOptions = toolOptions with { RowId = row };
            if (selected == Inspector.RetainerTimers)
            {
                ImGui.InputText("Remaining seconds (optional, user supplied)", ref remainingSeconds, 6);
                int? seconds = null;
                if (remainingSeconds.Length > 0)
                {
                    if (int.TryParse(remainingSeconds, out var value)) seconds = value;
                    else toolInputValid = false;
                }
                toolOptions = toolOptions with { RemainingSeconds = seconds };
                ImGui.TextWrapped("Task maximum duration is static metadata, not a running venture. Timer estimate uses supplied seconds; live retainer/deployable timers are deferred.");
            }
            if (selected == Inspector.Duty) ImGui.TextWrapped("Requirements only. Unlock, character average item level, reward mapping and launch readiness remain unavailable; no duty entry.");
        }
        if (selected == Inspector.Currency)
        {
            ImGui.InputText("Currency item ID", ref itemFilter, 10);
            toolInputValid &= OptionalId(itemFilter, out var item) && item != null;
            toolOptions = toolOptions with { ItemId = item };
            ImGui.TextWrapped("Currency container only. A missing row is not a zero count; special currencies and caps are not inferred.");
        }
        if (selected == Inspector.HousingSchedule)
        {
            ImGui.InputText("Entry-start epoch (ISO UTC, user supplied)", ref scheduleEpoch, 40);
            DateTimeOffset? epoch = null;
            if (scheduleEpoch.Length > 0)
            {
                if ((scheduleEpoch.EndsWith('Z') || scheduleEpoch.EndsWith("+00:00", StringComparison.Ordinal)) &&
                    DateTimeOffset.TryParse(scheduleEpoch, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) epoch = parsed.ToUniversalTime();
                else toolInputValid = false;
            }
            var entry = toolOptions.EntryDays; var results = toolOptions.ResultsDays;
            ImGui.InputInt("Entry days (supplied, 1–30)", ref entry);
            ImGui.InputInt("Results days (supplied, 1–30)", ref results);
            toolOptions = toolOptions with { ScheduleEpoch = epoch, EntryDays = entry, ResultsDays = results };
            ImGui.TextWrapped("No authoritative epoch assumed. Blank/future epoch yields ScheduleUndefined. This calculator does not observe lottery or bid state.");
        }
    }

    private static T EnumInput<T>(string label, T current) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Math.Max(0, Array.IndexOf(values, current));
        ImGui.SetNextItemWidth(180);
        ImGui.Combo(label, ref index, Enum.GetNames<T>(), values.Length);
        return values[index];
    }

    private static bool OptionalId(string text, out uint? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!uint.TryParse(text, out var id) || id == 0) return false;
        value = id; return true;
    }

    private static Guid? SnapshotInput(string label, Guid? current, CaptureResult[] snapshots)
    {
        var chosen = snapshots.FirstOrDefault(r => r.RequestId == current);
        if (chosen == null) current = null;
        if (ImGui.BeginCombo(label, chosen == null ? "Select retained capture" : SnapshotLabels.For(chosen)))
        {
            try
            {
                foreach (var result in snapshots.Reverse())
                    if (ImGui.Selectable($"{SnapshotLabels.For(result)}##{label}{result.RequestId}", current == result.RequestId))
                        current = result.RequestId;
            }
            finally { ImGui.EndCombo(); }
        }
        return current;
    }

    private void DrawResults(EngineView view)
    {
        var matching = view.Results.Where(r => r.Inspector == selected).ToArray();
        if (matching.Length == 0) { ImGui.TextWrapped("No retained results for this inspector. Press Capture."); return; }
        var latest = matching[^1];
        var lastSuccess = matching.LastOrDefault(r => r.Outcome == Outcome.Success);
        ImGui.TextUnformatted($"Latest attempt: {latest.Outcome} / {latest.Reason} — {latest.CompletedUtc:HH:mm:ss} UTC");
        ImGui.TextWrapped(lastSuccess == null ? "Last successful capture: none retained." :
            $"Last successful capture: {lastSuccess.CompletedUtc:O}{(lastSuccess.Generation != view.Generation ? " — STALE CONTEXT" : " — historical snapshot, not live")}{(lastSuccess.Partial ? " — PARTIAL" : "")}");
        var show = ImGui.BeginChild("history", new Vector2(0, 120), true);
        try
        {
            if (show)
            {
                foreach (var item in matching.Reverse())
                    if (ImGui.Selectable($"{item.CompletedUtc:HH:mm:ss} {item.Outcome}/{item.Reason}##{item.RequestId}", selectedResult == item.RequestId))
                        selectedResult = item.RequestId;
            }
        }
        finally { ImGui.EndChild(); }
        var result = matching.FirstOrDefault(r => r.RequestId == selectedResult) ?? latest;
        if (renderedResult != result.RequestId || renderedGeneration != view.Generation)
        {
            renderedResult = result.RequestId; renderedGeneration = view.Generation;
            resultText = ReportCodec.Describe(result, view.Generation, engine.Performance);
        }
        DrawPayloadTable(result.Payload);
        if (result.Payload is CallbackPayload) ImGui.TextWrapped(CallbackText.NumericWarning);
        if (ImGui.Button("Copy result"))
        {
            ImGui.SetClipboardText(resultText);
            status = result.Payload is CallbackPayload
                ? "Copied result; unredacted numeric callback arguments may contain IDs. Review before sharing."
                : "Copied result; display names and positions remain included.";
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(exportTask != null);
        try
        {
            if (ImGui.Button("Preview selected JSON")) PreparePreview(view, result, ReportFormat.Json);
            ImGui.SameLine();
            if (ImGui.Button("Preview selected text")) PreparePreview(view, result, ReportFormat.Text);
        }
        finally { ImGui.EndDisabled(); }
        if (preview != null)
        {
            ImGui.TextWrapped($"Preview frozen: {preview.FileName} ({preview.Bytes.Length} bytes). Writes only to Debug Kit's config-directory reports folder; no upload or overwrite. Numeric callback logs may contain IDs; review frozen content before export.");
            if (exportTask == null)
            {
                if (ImGui.Button("Export EXACT preview")) StartExport(preview);
                ImGui.SameLine();
                if (ImGui.Button("Open reports folder")) OpenReportsFolder();
                ImGui.SameLine();
                if (ImGui.Button("Close preview")) { preview = null; previewText = ""; }
            }
            else
            {
                if (ImGui.Button("Cancel export")) exportCancellation?.Cancel();
                ImGui.SameLine();
                if (ImGui.Button("Open reports folder")) OpenReportsFolder();
            }
        }
        // Keep results usable in short windows; the outer tool pane scrolls when controls need more room.
        show = ImGui.BeginChild("detail", new Vector2(0, MathF.Max(220, ImGui.GetContentRegionAvail().Y)), true);
        try { if (show) ImGui.TextUnformatted(preview == null ? resultText : previewText); }
        finally { ImGui.EndChild(); }
    }

    private void OpenReportsFolder()
    {
        try
        {
            Directory.CreateDirectory(exporter.DirectoryPath);
            using var process = Process.Start(new ProcessStartInfo(exporter.DirectoryPath) { UseShellExecute = true });
            status = "Opened reports folder.";
        }
        catch (Exception) { status = "Could not open reports folder."; }
    }

    private void DrawPayloadTable(ToolPayload? payload)
    {
        if (payload is CallbackPayload callbacks) { DrawCallbackTable(callbacks); return; }
        if (payload is not (AddonsPayload or InventoryPayload or SheetPayload or DomainPayload or DiagnosticFieldsPayload or PluginInventoryPayload or LookupPayload or ObservationPayload) || !ImGui.CollapsingHeader("Structured rows")) return;
        if (payload is SheetPayload or DomainPayload) ImGui.TextDisabled($"Text-shortened fields: {Payloads.ShortenedFields(payload)} (hover marked values for details)");
        if (!ImGui.BeginTable("payload-rows", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 180))) return;
        try
        {
            ImGui.TableSetupColumn("Key"); ImGui.TableSetupColumn("Value / state"); ImGui.TableSetupColumn("Details / navigation");
            ImGui.TableHeadersRow();
            switch (payload)
            {
                case AddonsPayload p:
                    foreach (var row in p.Rows)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Name} / ID {row.AddonId}");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"visible {row.Visible}; ready {row.Ready}");
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Values##{row.Name}{row.AddonId}")) Navigate(Inspector.AddonValues, new(Addon: row.Name));
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Nodes##{row.Name}{row.AddonId}")) Navigate(Inspector.AddonNodes, new(Addon: row.Name));
                    }
                    break;
                case InventoryPayload p:
                    foreach (var row in p.Rows)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Bag}/{row.Slot}");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Name ?? "empty/unavailable"} ({row.ItemId})");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"quantity {row.Quantity}; HQ {row.Hq}");
                    }
                    break;
                case SheetPayload p:
                    foreach (var row in p.Fields)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Field + (row.TextShortened ? " [shortened]" : ""));
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(FieldText.Display(row));
                        if (row.TextShortened && ImGui.IsItemHovered()) ImGui.SetTooltip(FieldText.Display(row));
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"{p.Sheet}/{p.RowId} [{p.Language}]");
                    }
                    break;
                case DiagnosticFieldsPayload p:
                    foreach (var row in p.Fields)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Section} / {row.Field}");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Value ?? "(no value)");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Type} | {row.Availability}");
                    }
                    break;
                case PluginInventoryPayload p:
                    foreach (var row in p.Rows)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(row.InternalName);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Loaded ? "Loaded" : "Not loaded");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Version);
                    }
                    break;
                case LookupPayload p:
                    foreach (var row in p.Rows)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{p.Kind} #{row.RowId}");
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(string.Join(" | ", row.Fields.Select(f => $"{f.Field}: {FieldText.Display(f)}")));
                        ImGui.TableNextColumn(); ImGui.TextUnformatted($"{p.Language} | {(p.Complete ? "bounded range complete" : "partial")}");
                    }
                    break;
                case ObservationPayload p:
                    ImGui.TextDisabled($"Retained {p.Samples.Length}/{ObservationPolicy.Capacity} samples | dropped {p.Dropped}");
                    foreach (var sample in p.Samples)
                        foreach (var row in sample.Fields)
                        {
                            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted($"{sample.ObservedUtc:HH:mm:ss.fff} {row.Section}/{row.Field}");
                            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Value ?? "(no value)");
                            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{row.Type} | {row.Availability}");
                        }
                    break;
                case DomainPayload p:
                    foreach (var row in p.Rows)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Key);
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Name + (row.Fields.Any(f => f.TextShortened) ? " [shortened text]" : ""));
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(string.Join(" | ", row.Fields.Select(f => $"{f.Field}: {FieldText.Display(f)}")));
                        if (row.Fields.Any(f => f.TextShortened) && ImGui.IsItemHovered())
                            ImGui.SetTooltip(string.Join("\n", row.Fields.Where(f => f.TextShortened).Select(f => $"{f.Field}: {FieldText.Display(f)}")));
                    }
                    break;
            }
        }
        finally { ImGui.EndTable(); }
    }

    private static void DrawCallbackTable(CallbackPayload payload)
    {
        ImGui.TextDisabled($"Observed events: {payload.Rows.Length} | older events dropped: {payload.EventsDropped} | read failures: {payload.ReadFailures} | stopped snapshot");
        if (!ImGui.BeginTable("callback-rows", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new Vector2(0, 240))) return;
        try
        {
            ImGui.TableSetupColumn("Addon", ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Values", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Raw flag", ImGuiTableColumnFlags.WidthFixed, 85);
            ImGui.TableSetupColumn("Return", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableHeadersRow();
            foreach (var row in payload.Rows)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(CallbackNames.Display(payload, row));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{row.ObservedUtc:O}\nArguments: {row.ArgumentCount}; entries omitted: {row.ArgumentsOmitted}\nFlag meaning not inferred; not a replay control.");
                ImGui.TableNextColumn();
                foreach (var argument in row.Arguments)
                {
                    var line = CallbackText.Argument(argument);
                    ImGui.TextUnformatted(line);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(line);
                }
                if (row.Arguments.IsEmpty) ImGui.TextDisabled(row.ArgumentCount == 0 ? "(no arguments)" : "(arguments unavailable)");
                if (row.ArgumentsOmitted > 0) ImGui.TextDisabled($"+{row.ArgumentsOmitted} argument entries omitted");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(CallbackText.Boolean(row.Flag));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(CallbackText.Boolean(row.Returned));
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void PreparePreview(EngineView view, CaptureResult result, ReportFormat format)
    {
        try
        {
            preview = ReportCodec.Create(view with { Results = ImmutableArray.Create(result) }, provenance, format, DateTimeOffset.UtcNow, engine.Performance);
            previewText = preview.Content;
            status = result.Payload is CallbackPayload
                ? "Review frozen preview: unredacted numeric callback arguments may contain IDs. Export includes them."
                : "Review frozen preview, then explicitly export. It contains display names, positions and territory.";
        }
        catch (Exception) { preview = null; previewText = ""; status = "Preview unavailable; no exception payload retained."; }
    }

    private void StartExport(ReportPreview frozen)
    {
        exportCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = exportCancellation.Token;
        exportTask = Task.Run(() => exporter.ExportAsync(frozen, token));
        status = "Export pending.";
    }

    private void ObserveExport()
    {
        if (exportTask is not { IsCompleted: true } task) return;
        status = task.Status == TaskStatus.RanToCompletion ? $"Export: {task.Result}." : "Export failed; error details omitted.";
        exportTask = null;
        exportCancellation?.Dispose(); exportCancellation = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        exportCancellation?.Cancel();
        // No continuation writes into UI; completion is observed only while this window is alive.
        exportCancellation?.Dispose();
        lifetime.Dispose();
        preview = null;
    }
}
