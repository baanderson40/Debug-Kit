using System.Collections.Immutable;
using System.Diagnostics;

namespace DebugKit.Core;

public enum Inspector { Context, NearbyObjects, Target, AddonList, AddonValues, AddonNodes, ContextMenu, SheetRow, Inventory, Compare,
    Fates, Casts, BozjaEvents, OccultEvents, WorldPlacements, CollisionBounds, FamiliarData, FamiliarUi,
    HousingSchedule, HousingUi, Duty, Currency, RetainerTimers, ProcessInfo, ProviderMetadata, ProviderReadiness, MarketSummary, HousingService, AddonCallbacks,
    BigDump, ConditionFlags, PlayerStatuses, InstalledPlugins, MovementSpeedTracker, TargetDistanceTracker, EntityNameWatch, JobLookup, NpcNameLookup, TerritoryLookup }
public enum ObjectCategory { Any, Player, BattleNpc, EventNpc, EventObject, Treasure, Companion, Other }
// SDK enum symbols only, not object names. Unrecognized future kinds retain their numeric value.
public enum NativeKindLabel
{
    Unknown, None, Pc, Player, BattleNpc, EventNpc, Treasure, Aetheryte, GatheringPoint,
    EventObj, Mount, Companion, Retainer, AreaObject, HousingObject, Cutscene,
    CardStand, Ornament, Housing, EventObject,
}
public static class NativeKinds
{
    public static NativeKindLabel Label(string? sdkEnumName) =>
        Enum.TryParse<NativeKindLabel>(sdkEnumName, out var label) && Enum.IsDefined(label)
            ? label : NativeKindLabel.Unknown;
}
public enum Outcome { Success, Blocked, Unavailable, Failed, Cancelled }
public enum Reason
{
    None, LoggedOut, Loading, PlayerUnavailable, SourceUnavailable, ObjectReadFailed,
    ContextChanged, UserCancelled, Disposed, EntryLimit, RowLimit, PayloadLimit,
    NoTarget, NativeOptInRequired, UnsupportedBuild, AddonUnavailable, AddonUnready, SourceChanged,
    RowNotFound, ContainerUnavailable, InvalidRange, SnapshotUnavailable, IncompatibleSnapshots,
    UnsupportedContext, DecoderDeferred, ScheduleUndefined, AssetUnavailable,
    SheetSchemaMismatch, SheetLanguageUnavailable, SheetUnavailable, SheetVariantUnsupported, SheetFieldReadFailed,
    ProviderUnavailable, UnsupportedContract, IpcMissing, IpcFailed,
    NetworkConsentRequired, NetworkRateLimited, NetworkTimeout, ResponseTooLarge, NetworkSchemaMismatch, NetworkHttpError, NetworkFailed,
    CallbackOptInRequired, ObservationTimedOut,
}
public enum QueueStatus { Accepted, Duplicate, Full, Invalid, Disposed }
public enum ReportFormat { Json, Text }

public sealed record Descriptor(Inspector Inspector, string Id, int Version, string Title, string Category,
    string Help, bool RequiresPlayer, bool BulkSafe);

public static class Catalog
{
    public static ImmutableArray<Descriptor> All { get; } =
    [
        new(Inspector.Context, "game.context", 1, "Game Context", "Game State",
            "Read login, territory, readiness, position, job/level, and selected conditions. No identifiers or names.", false, true),
        new(Inspector.NearbyObjects, "objects.nearby", 3, "Nearby Objects", "Objects",
            "Bounded object-table scan with in-game display names, including players and companions. Object IDs, owner IDs, and addresses are omitted. Multi-step captures are non-atomic.", true, true),
        new(Inspector.Target, "objects.target", 1, "Selected Target", "Objects", "Read the selected target; never selects or changes a target. Names are included.", false, false),
        new(Inspector.AddonList, "addons.list", 1, "Addon List", "Addons & UI", "Bounded loaded/visible/focused addon metadata. Exact native compatibility and enabled session authorization required.", false, false),
        new(Inspector.AddonValues, "addons.values", 1, "Addon Values", "Addons & UI", "Inspect value types and reviewed scalar fields. Unclassified numeric/string/pointer values are omitted, not dereferenced.", false, false),
        new(Inspector.AddonNodes, "addons.nodes", 1, "Addon Nodes", "Addons & UI", "Bounded root ULD node-list geometry with depth/cycle checks. Component internals and arbitrary node text are not decoded.", false, false),
        new(Inspector.ContextMenu, "menus.context", 1, "Open Context Menu", "Addons & UI", "Capture an already-open ContextMenu. Never opens or clicks it; action mapping/enabled state may be unavailable.", false, false),
        new(Inspector.SheetRow, "sheets.row", 2, "Sheet Row", "Game Data", "Inspect one installed game-data row using explicit typed field readers. Shortened field text is disclosed; no arbitrary reflection or reference traversal.", false, false),
        new(Inspector.Inventory, "inventory.slots", 1, "Inventory Slots", "Inventory", "Read normal player bags. Unloaded containers remain unavailable. No moves, sorting, sale, or discard.", true, false),
        new(Inspector.Compare, "results.compare", 2, "Snapshot Comparison", "Results", "Compare compatible captures. Shortened source text is indeterminate, not definitive equality. Partial observations never prove removals.", false, false),
        new(Inspector.Fates, "content.fates", 2, "Active FATEs", "Content", "Public FATE observations, not join/sync controls. Missing or unreadable entries never establish absence.", true, false),
        new(Inspector.Casts, "objects.casts", 2, "Observed Casts", "Objects", "Bounded public cast observations. No target IDs, action dispatch or hooks; names included.", true, false),
        new(Inspector.BozjaEvents, "content.bozja.events", 2, "Bozjan Event Evidence", "Content", "Current Bozja/Zadnor FATE evidence. CE runtime state and links are deferred, not inferred from markers.", true, false),
        new(Inspector.OccultEvents, "content.occult.events", 2, "Occult Event Evidence", "Content", "Current Occult Crescent FATE evidence. CE runtime state is unavailable; no event selection or travel.", true, false),
        new(Inspector.WorldPlacements, "world.placements", 2, "Static World Placements", "World", "Bounded installed LGB placements near player. Static data, not spawned/loaded runtime objects; no asset paths exported.", true, false),
        new(Inspector.CollisionBounds, "world.collision", 2, "Static Collision Bounds", "World", "Installed LGB collision-box transforms/shapes only. Radius checks placement origin distance, not bounds overlap. No runtime navigation claims.", true, false),
        new(Inspector.FamiliarData, "content.familiars.data", 2, "Familiar Sheet Evidence", "Familiars", "Explicit hash-validated XBM sheet fields. Raw Column/Unknown labels do not imply stats, ownership or roster membership. No familiar job required.", false, false),
        new(Inspector.FamiliarUi, "content.familiars.ui", 2, "Familiar UI Metadata", "Familiars", "Already-loaded XBM addon metadata only. Roster/stage/treasure semantic decoders deferred; no entry or card selection.", false, false),
        new(Inspector.HousingSchedule, "housing.schedule", 2, "Housing Schedule Calculator", "Housing", "Calculate UTC phases from a user-supplied entry-start epoch/durations. Not authoritative game lottery state; no default epoch guessed.", false, false),
        new(Inspector.HousingUi, "housing.ui", 2, "Housing UI Metadata", "Housing", "Loaded housing-addon metadata only. Plot/bid/entry state is not decoded. No chat capture, bid or purchase.", false, false),
        new(Inspector.Duty, "content.duty", 2, "Duty Requirements", "Content", "Static ContentFinderCondition fields. Raw QueueMaxPlayers is not interpreted party size; unlock/launch/provider readiness unavailable.", false, false),
        new(Inspector.Currency, "content.currency", 2, "Observed Currency", "Inventory", "Normal currency-container slots only, one item ID. No currency/item changes. Counts are observations; caps unavailable.", true, false),
        new(Inspector.RetainerTimers, "content.retainer.task", 2, "Retainer Task & Timer", "Retainers", "Static RetainerTask requirements plus optional user-supplied timer estimate. No live retainer/other-character/private-provider state.", false, false),
        new(Inspector.ProcessInfo, "system.process", 2, "Process Memory", "System", "Current process managed/working-set/private memory only. No identities, paths, handles, trimming or priority changes.", false, false),
        new(Inspector.ProviderMetadata, "providers.metadata", 2, "Provider Metadata", "Providers", "Fixed provider catalog using public Dalamud metadata only. Versions/dev flags are advisory; loaded does not establish endpoint presence or readiness. No IPC calls or controls.", false, false),
        new(Inspector.ProviderReadiness, "providers.readiness", 2, "Provider State Query", "Providers", "One fixed source-reviewed boolean endpoint; metadata version differences do not block. Live endpoint/signature failures remain unavailable. Installed implementation semantics are not certified. No polling or controls.", false, false),
        new(Inspector.MarketSummary, "network.universalis", 2, "Universalis Summary", "Network", "Explicit cached NQ/HQ summary for one user-supplied item/world or fixed DC. External observations, not authoritative current prices. No individual listings or identity uploads.", false, false),
        new(Inspector.HousingService, "network.housing", 2, "Housing Service Summary", "Network", "Explicit PaissaDB district count/observation-age summary. Reported open plots do not establish current purchase/entry availability. No authentication, contributions, bids or controls.", false, false),
        new(Inspector.AddonCallbacks, "addons.callbacks", 4, "Addon Callback Log", "Addons & UI", "Manually start/stop callback observation for all addons or one exact name. Newest 64 events retained. Unredacted integers may contain IDs. Strings/pointers excluded; no replay.", true, false),
        new(Inspector.BigDump, "game.big-dump", 1, "Big Dump", "Game State", "Reviewed player/client/target fields only; no reflection, object IDs, pointers, or recursive serialization. Unsupported fields are labeled.", false, false),
        new(Inspector.ConditionFlags, "game.condition-flags", 1, "Condition Flags", "Game State", "Snapshot current SDK condition flags and values. No actions, chat messages, or automatic tracking.", false, false),
        new(Inspector.PlayerStatuses, "game.player-statuses", 1, "Player Statuses", "Game State", "Snapshot local-player status IDs, stacks, and durations. Source object IDs excluded.", true, false),
        new(Inspector.InstalledPlugins, "plugins.installed", 1, "Installed Plugins", "Providers", "Bounded public Dalamud plugin inventory; internal names/load state only. No IPC or plugin configuration reads.", false, false),
        new(Inspector.JobLookup, "sheets.job-lookup", 1, "Job Lookup", "Game Data", "Explicit ClassJob name and abbreviation lookup.", false, false),
        new(Inspector.NpcNameLookup, "sheets.npc-name", 1, "NPC Name Lookup", "Game Data", "Typed NPC name lookup with explicit source sheet; no guessed cross-sheet relationships.", false, false),
        new(Inspector.TerritoryLookup, "sheets.territory-name", 1, "Territory Lookup", "Game Data", "Resolve an explicit TerritoryType PlaceName reference.", false, false),
        new(Inspector.MovementSpeedTracker, "observe.movement-speed", 1, "Movement Speed Tracker", "Game State", "Manual bounded observation; sampled displacement/time, not authoritative movement speed.", true, false),
        new(Inspector.TargetDistanceTracker, "observe.target-distance", 1, "Target Distance Tracker", "Objects", "Manual bounded distance observations; no target changes or identity-continuity claims.", false, false),
        new(Inspector.EntityNameWatch, "observe.entity-name", 1, "Entity Name Watch", "Objects", "Manual bounded name-filtered observations. Not observed does not prove despawn; no map flags.", true, false),
    ];
    private static readonly IReadOnlyDictionary<Inspector, Descriptor> ByInspector = All.ToDictionary(x => x.Inspector);
    public static Descriptor Get(Inspector inspector) => ByInspector[inspector];
}

public readonly record struct Point(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    public double Distance(Point other)
    {
        var x = (double)X - other.X;
        var y = (double)Y - other.Y;
        var z = (double)Z - other.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }
}

public sealed record Conditions(bool InCombat, bool Casting, bool Mounted, bool Occupied, bool BetweenAreas);

// Context deliberately excludes names, character IDs, pointers, and exception payloads.
public sealed record ContextData(bool LoggedIn, uint? TerritoryId, bool PlayerAvailable, Point? Position,
    uint? JobId, int? Level, Conditions Conditions);

// Transient identity exists only for lifetime invalidation; never placed in a CaptureResult or report.
public readonly record struct LiveContext(ContextData Data, ulong CharacterToken);
public readonly record struct ObjectSample(ObjectCategory Kind, uint? BaseId, Point Position, bool Targetable, bool Valid,
    byte? RawKindValue = null, NativeKindLabel RawKindLabel = NativeKindLabel.Unknown, string? Name = null);
public sealed record ObjectRow(ObjectCategory Kind, uint? BaseId, Point Position, double? Distance, bool Targetable,
    byte? RawKindValue = null, NativeKindLabel RawKindLabel = NativeKindLabel.Unknown, string? Name = null);
public sealed record CaptureOptions(float Radius = 60, ObjectCategory Kind = ObjectCategory.Any, uint? BaseId = null)
{
    public bool IsValid => float.IsFinite(Radius) && Radius is >= 1 and <= 200 &&
        Enum.IsDefined(Kind) && BaseId != 0;
}

public sealed record CaptureResult(Guid RequestId, Inspector Inspector, int SchemaVersion, long Generation,
    DateTimeOffset RequestedUtc, DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc, double DurationMs,
    Outcome Outcome, Reason Reason, bool Partial, bool NonAtomic, int Steps,
    int Examined, int Matched, int Empty, int Invalid, int Filtered, int Unavailable, int Omitted,
    CaptureOptions? Options, ContextData? Context, ImmutableArray<ObjectRow> Objects,
    ToolOptions? ToolOptions = null, ToolPayload? Payload = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? HttpStatusCode = null)
{
    // Retain the aggregate in JSON for existing consumers; display the separate causes.
    public int Skipped => Empty + Invalid + Filtered;
}

public sealed record EngineView(long Generation, int Pending, Guid? ActiveRequest, int Examined,
    ImmutableArray<CaptureResult> Results, long Evicted, long Dropped, long Rejected, bool CommandAvailable);

public sealed record Limits(int Queue = 16, int Rows = 128, int EntriesPerStep = 32, int Entries = 1024,
    int Results = 100, int ResultBytes = 65536, int SessionBytes = 1048576, double StepMilliseconds = 2)
{
    public void Validate()
    {
        if (Queue is < 1 or > 16 || Rows is < 1 or > 128 || EntriesPerStep is < 1 or > 32 ||
            Entries is < 1 or > 1024 || Results is < 1 or > 100 || ResultBytes is < 2048 or > 65536 ||
            SessionBytes < ResultBytes || SessionBytes > 1048576 || !double.IsFinite(StepMilliseconds) ||
            StepMilliseconds is <= 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(Limits));
    }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    long Timestamp { get; }
    double ElapsedMilliseconds(long since);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long Timestamp => Stopwatch.GetTimestamp();
    public double ElapsedMilliseconds(long since) => Stopwatch.GetElapsedTime(since).TotalMilliseconds;
}

// Implementations must read on the game framework thread, return detached value data, and never control game state.
public interface IGameSource
{
    LiveContext ReadContext();
    int ObjectTableLength { get; }
    ObjectSample? ReadObject(int index);
}
