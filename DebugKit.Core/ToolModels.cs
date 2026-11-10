using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DebugKit.Core;

public enum AddonScope { Loaded, Visible, Focused }
public enum SheetKind { Item, Action, Mount, TerritoryType, BNpcName, ENpcResident, EObj, ClassJob, PlaceName,
    Fate, DynamicEvent, ContentFinderCondition, XBMPet, XBMItem, XBMContent, HousingPlacement, HousingUnplacement, RetainerTask }
public enum DataLanguage { Client, English, Japanese, German, French }
public enum InventoryBag { All, Bag1, Bag2, Bag3, Bag4 }
public enum ItemQuality { Any, Nq, Hq }
public enum ChangeKind { Added, Removed, Changed, Unchanged, Unobserved, Ambiguous, Indeterminate }

public sealed record ToolOptions(AddonScope Scope = AddonScope.Loaded, string Addon = "ContextMenu", int Instance = 1,
    int Start = 0, int Count = 64, int Depth = 8, uint? NodeId = null, SheetKind Sheet = SheetKind.Item,
    uint RowId = 1, DataLanguage Language = DataLanguage.Client, InventoryBag Bag = InventoryBag.All,
    ItemQuality Quality = ItemQuality.Any, uint? ItemId = null, bool IncludeEmpty = false,
    bool ExperimentalNative = false, Guid? Baseline = null, Guid? Comparison = null, bool ChangesOnly = true,
    float DomainRadius = 200, uint? FilterId = null, string NameFilter = "", int? StateFilter = null,
    bool SelectedOnly = false, WorldAsset Asset = WorldAsset.Planevent, PlacementKind Placement = PlacementKind.All,
    DateTimeOffset? ScheduleEpoch = null, int EntryDays = 5, int ResultsDays = 4, int? RemainingSeconds = null,
    ProviderKind Provider = ProviderKind.AutoRetainer, bool NetworkConsent = false, uint WorldId = 74,
    MarketScope MarketScope = MarketScope.World, MarketDataCenter DataCenter = MarketDataCenter.Aether,
    HousingDistrict District = HousingDistrict.Mist, float ObservationInterval = 0.5f,
    float MinimumDistance = 0.5f, int RollingSamples = 5, string ObservationName = "", bool ExactName = true,
    bool OnlyWhenMoving = true, bool ListJobs = false)
{
    public bool IsValid(Inspector inspector) => inspector switch
    {
        Inspector.Target => true,
        Inspector.ProviderMetadata => true,
        Inspector.BigDump or Inspector.ConditionFlags or Inspector.PlayerStatuses or Inspector.InstalledPlugins => true,
        Inspector.JobLookup => Enum.IsDefined(Language) && (ListJobs || RowId > 0),
        Inspector.TerritoryLookup => Enum.IsDefined(Language) && RowId > 0,
        Inspector.NpcNameLookup => Enum.IsDefined(Language) && RowId > 0 && Sheet is SheetKind.BNpcName or SheetKind.ENpcResident,
        Inspector.MovementSpeedTracker => float.IsFinite(ObservationInterval) && ObservationInterval is >= 0.1f and <= 5 &&
            float.IsFinite(MinimumDistance) && MinimumDistance is >= 0 and <= 20 && RollingSamples is >= 1 and <= 30,
        Inspector.TargetDistanceTracker => float.IsFinite(ObservationInterval) && ObservationInterval is >= 0.1f and <= 5,
        Inspector.EntityNameWatch => float.IsFinite(ObservationInterval) && ObservationInterval is >= 0.1f and <= 10 &&
            ObservationName is { Length: >= 1 and <= 64 } && ObservationName.All(c => !char.IsControl(c)),
        Inspector.ProviderReadiness => Enum.IsDefined(Provider),
        Inspector.AddonCallbacks => ValidAddonFilter(Addon) && Addon.Length <= 31,
        _ when NetworkPolicy.IsNetwork(inspector) => NetworkPolicy.Valid(inspector, this),
        Inspector.AddonList => Enum.IsDefined(Scope) && ValidAddonFilter(Addon),
        Inspector.AddonValues => ValidAddon(Addon) && Instance is >= 1 and <= 8 && Start is >= 0 and <= 4095 && Count is >= 1 and <= 128 && Start <= 4096 - Count,
        Inspector.AddonNodes => ValidAddon(Addon) && Instance is >= 1 and <= 8 && Depth is >= 1 and <= 8,
        Inspector.ContextMenu => Instance is >= 1 and <= 8,
        Inspector.SheetRow => Enum.IsDefined(Sheet) && Enum.IsDefined(Language),
        Inspector.Inventory => Enum.IsDefined(Bag) && Enum.IsDefined(Quality) && ItemId != 0,
        Inspector.Compare => Baseline is { } a && Comparison is { } b && a != Guid.Empty && b != Guid.Empty && a != b,
        _ when DomainPolicies.IsDomain(inspector) => DomainPolicies.Valid(inspector, this),
        _ => false,
    };

    public ToolOptions Canonical(Inspector inspector) => inspector switch
    {
        Inspector.Target => new(),
        Inspector.ProviderMetadata => new(),
        Inspector.BigDump or Inspector.ConditionFlags or Inspector.PlayerStatuses or Inspector.InstalledPlugins => new(),
        Inspector.JobLookup => new(RowId: RowId, Language: Language, Sheet: SheetKind.ClassJob, ListJobs: ListJobs),
        Inspector.NpcNameLookup => new(RowId: RowId, Language: Language, Sheet: Sheet),
        Inspector.TerritoryLookup => new(RowId: RowId, Language: Language, Sheet: SheetKind.TerritoryType),
        Inspector.MovementSpeedTracker => new(ObservationInterval: ObservationInterval, MinimumDistance: MinimumDistance, RollingSamples: RollingSamples, OnlyWhenMoving: OnlyWhenMoving),
        Inspector.TargetDistanceTracker => new(ObservationInterval: ObservationInterval),
        Inspector.EntityNameWatch => new(ObservationInterval: ObservationInterval, ObservationName: ObservationName, ExactName: ExactName),
        Inspector.ProviderReadiness => new(Provider: Provider),
        Inspector.AddonCallbacks => new(Addon: Addon, ExperimentalNative: ExperimentalNative),
        Inspector.MarketSummary => new(ItemId: ItemId, NetworkConsent: NetworkConsent, WorldId: MarketScope == MarketScope.World ? WorldId : 74,
            MarketScope: MarketScope, DataCenter: MarketScope == MarketScope.DataCenter ? DataCenter : MarketDataCenter.Aether),
        Inspector.HousingService => new(NetworkConsent: NetworkConsent, WorldId: WorldId, District: District),
        Inspector.AddonList => new(Scope: Scope, Addon: Addon, ExperimentalNative: ExperimentalNative),
        Inspector.AddonValues => new(Addon: Addon, Instance: Instance, Start: Start, Count: Count, ExperimentalNative: ExperimentalNative),
        Inspector.AddonNodes => new(Addon: Addon, Instance: Instance, Depth: Depth, NodeId: NodeId, ExperimentalNative: ExperimentalNative),
        Inspector.ContextMenu => new(Addon: "ContextMenu", Instance: Instance, ExperimentalNative: ExperimentalNative),
        Inspector.SheetRow => new(Sheet: Sheet, RowId: RowId, Language: Language),
        Inspector.Inventory => new(Bag: Bag, Quality: Quality, ItemId: ItemId, IncludeEmpty: IncludeEmpty, ExperimentalNative: ExperimentalNative),
        Inspector.Compare => new(Baseline: Baseline, Comparison: Comparison, ChangesOnly: ChangesOnly),
        _ when DomainPolicies.IsDomain(inspector) => DomainPolicies.Canonical(inspector, this),
        _ => this,
    };

    private static bool ValidAddon(string? text) => text is { Length: >= 1 and <= 64 } && Regex.IsMatch(text, @"\A[A-Za-z0-9_]+\z", RegexOptions.CultureInvariant);
    private static bool ValidAddonFilter(string? text) => text is { Length: <= 64 } && (text.Length == 0 || ValidAddon(text));
}

public enum TargetStatusAvailability { Available, Empty, Unsupported, Failed }
public sealed record TargetStatusData(uint? StatusId, string? Name, float? RemainingTime, bool ReadFailed = false);
public sealed record TargetData(ObjectRow Object, float? Rotation, float? HitboxRadius, uint? Hp, uint? MaxHp, uint? JobId, int? Level,
    TargetStatusAvailability StatusAvailability = TargetStatusAvailability.Unsupported,
    ImmutableArray<TargetStatusData> Statuses = default, int? StatusCount = null, bool StatusesTruncated = false);
public sealed record AddonRow(string Name, ushort AddonId, bool Visible, bool Ready, int? Values, int? Nodes);
public sealed record ValueRow(int Index, string Type, string Value, bool Decoded);
public sealed record NodeRow(int ListIndex, uint NodeId, ushort Type, int Depth, bool Visible, float X, float Y, ushort Width, ushort Height, bool DepthLimited);
public sealed record FieldRow(string Field, string Value, bool TextShortened = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OriginalTextLength = null);
public sealed record InventoryRow(InventoryBag Bag, int Slot, uint ItemId, string? Name, int Quantity, bool Hq);
public enum FieldAvailability { Available, Absent, Unsupported, Withheld, Failed, NotApplicable }
public sealed record DiagnosticField(string Section, string Field, string Type, string? Value, FieldAvailability Availability);
public sealed record DiagnosticFieldsPayload(ImmutableArray<DiagnosticField> Fields, string Source, bool NonAtomic = false) : ToolPayload;
public sealed record PluginInventoryRow(string InternalName, bool Loaded, string Version);
public sealed record PluginInventoryPayload(ImmutableArray<PluginInventoryRow> Rows, bool Complete) : ToolPayload;
public sealed record LookupRow(uint RowId, ImmutableArray<FieldRow> Fields);
public sealed record LookupPayload(Inspector Kind, string Language, ImmutableArray<LookupRow> Rows, bool Complete, string Source) : ToolPayload;
public sealed record ObservationSample(DateTimeOffset ObservedUtc, ImmutableArray<DiagnosticField> Fields);
public sealed record ObservationPayload(ImmutableArray<ObservationSample> Samples, long Dropped, string Description) : ToolPayload;
public sealed record DifferenceRow(string Key, ChangeKind Change, string? Before, string? After,
    bool BeforeShortened = false, bool AfterShortened = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TargetPayload), "target")]
[JsonDerivedType(typeof(AddonsPayload), "addons")]
[JsonDerivedType(typeof(ValuesPayload), "values")]
[JsonDerivedType(typeof(NodesPayload), "nodes")]
[JsonDerivedType(typeof(MenuPayload), "menu")]
[JsonDerivedType(typeof(SheetPayload), "sheet")]
[JsonDerivedType(typeof(InventoryPayload), "inventory")]
[JsonDerivedType(typeof(ComparisonPayload), "comparison")]
[JsonDerivedType(typeof(DomainPayload), "domain")]
[JsonDerivedType(typeof(ProviderPayload), "provider")]
[JsonDerivedType(typeof(ProviderQueryPayload), "providerQuery")]
[JsonDerivedType(typeof(NetworkPayload), "network")]
[JsonDerivedType(typeof(CallbackPayload), "callbacks")]
[JsonDerivedType(typeof(DiagnosticFieldsPayload), "diagnosticFields")]
[JsonDerivedType(typeof(PluginInventoryPayload), "pluginInventory")]
[JsonDerivedType(typeof(LookupPayload), "lookup")]
[JsonDerivedType(typeof(ObservationPayload), "observation")]
public abstract record ToolPayload;
public sealed record TargetPayload(TargetData Target) : ToolPayload;
public sealed record AddonsPayload(ImmutableArray<AddonRow> Rows, string GameBuild, string Profile) : ToolPayload;
public sealed record ValuesPayload(string Addon, int Instance, long Lifetime, ImmutableArray<ValueRow> Rows, string GameBuild) : ToolPayload;
public sealed record NodesPayload(string Addon, int Instance, long Lifetime, ImmutableArray<NodeRow> Rows, string GameBuild) : ToolPayload;
public sealed record MenuPayload(int Instance, long Lifetime, ImmutableArray<ValueRow> Rows, string GameBuild) : ToolPayload;
public sealed record SheetPayload(SheetKind Sheet, uint RowId, string Language, ImmutableArray<FieldRow> Fields, string GameBuild) : ToolPayload;
public sealed record InventoryPayload(ImmutableArray<InventoryRow> Rows, string GameBuild) : ToolPayload;
public sealed record ComparisonPayload(Guid Baseline, Guid Comparison, ImmutableArray<DifferenceRow> Rows,
    int Added, int Removed, int Changed, int Unchanged, int Unobserved, int Ambiguous, bool CompleteEvidence, int Indeterminate = 0) : ToolPayload;

public sealed record ToolStep(ToolPayload? Payload, int NextCursor = 0, bool Complete = true, bool Partial = false,
    Outcome Outcome = Outcome.Success, Reason Reason = Reason.None, long? SourceStamp = null,
    int Examined = 0, int Matched = 0, int Empty = 0, int Invalid = 0, int Filtered = 0, int Unavailable = 0,
    int? HttpStatusCode = null);

public interface IToolSource
{
    ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? expectedStamp);
}

public interface IObservationToolSource
{
    ToolStep SampleObservation(Inspector inspector, ToolOptions options);
    void EndObservation(Inspector inspector);
}

public static class ReportText
{
    public static string Clean(string? value) => ObjectNames.For(ObjectCategory.EventNpc, value) ?? "unavailable";
}

public static class Payloads
{
    public static ToolPayload? Sanitize(ToolPayload? payload) => payload switch
    {
        TargetPayload p when p.Target.Object.Position.IsFinite => p with { Target = p.Target with
        {
            Object = p.Target.Object with
            {
                Name = ObjectNames.For(p.Target.Object.Kind, p.Target.Object.Name),
                BaseId = p.Target.Object.Kind is ObjectCategory.Player or ObjectCategory.Other ? null : p.Target.Object.BaseId,
                Distance = p.Target.Object.Distance is { } d && double.IsFinite(d) && d >= 0 ? d : null,
            },
            Rotation = p.Target.Rotation is { } r && float.IsFinite(r) ? r : null,
            HitboxRadius = p.Target.HitboxRadius is { } h && float.IsFinite(h) && h >= 0 ? h : null,
            StatusAvailability = Enum.IsDefined(p.Target.StatusAvailability) ? p.Target.StatusAvailability : TargetStatusAvailability.Failed,
            Statuses = (p.Target.Statuses.IsDefault ? ImmutableArray<TargetStatusData>.Empty : p.Target.Statuses)
                .Take(80).Select(s => s with
                {
                    Name = s.Name == null ? null : ObjectNames.For(ObjectCategory.EventNpc, s.Name),
                    RemainingTime = s.RemainingTime is { } time && float.IsFinite(time) ? time : null,
                }).ToImmutableArray(),
            StatusCount = p.Target.StatusCount is >= 0 and <= 10000 ? p.Target.StatusCount : null,
            StatusesTruncated = p.Target.StatusesTruncated || (p.Target.StatusCount is { } count && count > 80),
        } },
        AddonsPayload p => p with { Rows = p.Rows.Take(256).Select(r => r with { Name = ReportText.Clean(r.Name) }).ToImmutableArray(), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild), Profile = p.Profile == "ExperimentalMatched" ? p.Profile : "Unsupported" },
        ValuesPayload p => p with { Rows = p.Rows.Take(128).Select(r => r with { Type = ReportText.Clean(r.Type), Value = ReportText.Clean(r.Value) }).ToImmutableArray(), Addon = ReportText.Clean(p.Addon), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild) },
        NodesPayload p => p with { Rows = p.Rows.Where(r => float.IsFinite(r.X) && float.IsFinite(r.Y)).Take(256).ToImmutableArray(), Addon = ReportText.Clean(p.Addon), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild) },
        MenuPayload p => p with { Rows = p.Rows.Take(64).Select(r => r with { Type = ReportText.Clean(r.Type), Value = ReportText.Clean(r.Value) }).ToImmutableArray(), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild) },
        SheetPayload p => p with { Fields = p.Fields.Take(64).Select(FieldText.Sanitize).ToImmutableArray(), Language = ReportText.Clean(p.Language), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild) },
        InventoryPayload p => p with { Rows = p.Rows.Take(256).Select(r => r with { Name = ObjectNames.For(ObjectCategory.EventNpc, r.Name) }).ToImmutableArray(), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild) },
        ComparisonPayload p => p with { Rows = p.Rows.Take(256).Select(SanitizeDifference).ToImmutableArray() },
        ProviderPayload p => p with { Source = ReportText.Clean(p.Source), Rows = p.Rows.Take(32)
            .Where(r => Enum.IsDefined(r.Provider)).Select(r => r with { Version = ProviderCatalog.SafeVersion(r.Version) }).ToImmutableArray() },
        ProviderQueryPayload p when Enum.IsDefined(p.Provider.Provider) && Enum.IsDefined(p.Query) => p with
        { Provider = p.Provider with { Version = ProviderCatalog.SafeVersion(p.Provider.Version) },
            Endpoint = ProviderCatalog.Contracts.FirstOrDefault(x => x.Provider == p.Provider.Provider && x.Query == p.Query)?.Endpoint ?? "unavailable" },
        NetworkPayload p when Enum.IsDefined(p.Service) => p with { Rows = p.Rows.Take(8).Select(r => r with
        { Key = ReportText.Clean(r.Key), Name = ReportText.Clean(r.Name), Fields = r.Fields.Take(16).Select(FieldText.Sanitize).ToImmutableArray() }).ToImmutableArray() },
        CallbackPayload p => p with { Addon = p.Addon.Length == 0 ? "" : ReportText.Clean(p.Addon), GameBuild = NativeCompatibility.SafeBuild(p.GameBuild),
            Rows = p.Rows.Take(CallbackBuffer.MaximumEvents).Select(CallbackBuffer.Sanitize).ToImmutableArray(),
            EventsDropped = Math.Max(0, p.EventsDropped), ReadFailures = Math.Max(0, p.ReadFailures) },
        DiagnosticFieldsPayload p => p with { Source = ReportText.Clean(p.Source), Fields = p.Fields.Take(256).Select(f => f with
            { Section = ReportText.Clean(f.Section), Field = ReportText.Clean(f.Field), Type = ReportText.Clean(f.Type),
              Value = f.Value == null ? null : FieldText.Create(f.Field, f.Value).Value ?? string.Empty }).ToImmutableArray() },
        PluginInventoryPayload p => p with { Rows = p.Rows.Take(512).Select(r => r with
            { InternalName = ReportText.Clean(r.InternalName), Version = ProviderCatalog.SafeVersion(r.Version) ?? "Unavailable" }).ToImmutableArray() },
        LookupPayload p when p.Kind is Inspector.JobLookup or Inspector.NpcNameLookup or Inspector.TerritoryLookup => p with
            { Language = ReportText.Clean(p.Language), Source = ReportText.Clean(p.Source), Rows = p.Rows.Take(128).Select(r => r with
                { Fields = r.Fields.Take(8).Select(FieldText.Sanitize).ToImmutableArray() }).ToImmutableArray() },
        ObservationPayload p => p with { Description = ReportText.Clean(p.Description), Dropped = Math.Max(0, p.Dropped),
            Samples = p.Samples.TakeLast(128).Select(s => s with { Fields = s.Fields.Take(64).Select(f => f with
                { Section = ReportText.Clean(f.Section), Field = ReportText.Clean(f.Field), Type = ReportText.Clean(f.Type),
                  Value = f.Value == null ? null : FieldText.Create(f.Field, f.Value).Value ?? string.Empty }).ToImmutableArray() }).ToImmutableArray() },
        DomainPayload p => p with { Source = ReportText.Clean(p.Source), Language = ReportText.Clean(p.Language),
            GameBuild = NativeCompatibility.SafeBuild(p.GameBuild), Limitations = ReportText.Clean(p.Limitations),
            FilterOrigin = p.FilterOrigin is { IsFinite: true } origin ? origin : null,
            Rows = p.Rows.Take(257).Select(r => r with { Key = ReportText.Clean(r.Key), Name = ReportText.Clean(r.Name),
                Fields = r.Fields.Take(16).Select(FieldText.Sanitize).ToImmutableArray() }).ToImmutableArray() },
        _ => null,
    };
    private static DifferenceRow SanitizeDifference(DifferenceRow row)
    {
        var before = row.Before == null ? null : FieldText.Create("before", row.Before);
        var after = row.After == null ? null : FieldText.Create("after", row.After);
        return row with { Key = ReportText.Clean(row.Key), Before = before?.Value, After = after?.Value,
            BeforeShortened = row.BeforeShortened || before?.TextShortened == true,
            AfterShortened = row.AfterShortened || after?.TextShortened == true };
    }

    public static int ShortenedFields(ToolPayload? payload) => payload switch
    {
        SheetPayload p => p.Fields.Count(f => f.TextShortened),
        DomainPayload p => p.Rows.Sum(r => r.Fields.Count(f => f.TextShortened)),
        NetworkPayload p => p.Rows.Sum(r => r.Fields.Count(f => f.TextShortened)),
        _ => 0,
    };
    public static int Count(ToolPayload? payload) => payload switch
    {
        TargetPayload => 1, AddonsPayload p => p.Rows.Length, ValuesPayload p => p.Rows.Length,
        NodesPayload p => p.Rows.Length, MenuPayload p => p.Rows.Length, SheetPayload p => p.Fields.Length,
        InventoryPayload p => p.Rows.Length, ComparisonPayload p => p.Rows.Length, DomainPayload p => p.Rows.Length,
        ProviderPayload p => p.Rows.Length, ProviderQueryPayload => 1, NetworkPayload p => p.Rows.Length,
        CallbackPayload p => p.Rows.Length, DiagnosticFieldsPayload p => p.Fields.Length,
        PluginInventoryPayload p => p.Rows.Length, LookupPayload p => p.Rows.Length, ObservationPayload p => p.Samples.Length, _ => 0,
    };

    public static ToolPayload? Merge(ToolPayload? old, ToolPayload? next) => (old, next) switch
    {
        (null, _) => next, (_, null) => old,
        (AddonsPayload a, AddonsPayload b) => b with { Rows = a.Rows.AddRange(b.Rows) },
        (InventoryPayload a, InventoryPayload b) => b with { Rows = a.Rows.AddRange(b.Rows) },
        (DomainPayload a, DomainPayload b) when DomainPolicies.SameEvidence(a, b) => b with { Rows = a.Rows.AddRange(b.Rows) },
        (ObservationPayload a, ObservationPayload b) => ObservationPolicy.Append(a, b),
        (LookupPayload a, LookupPayload b) when a.Kind == b.Kind && a.Language == b.Language && a.Source == b.Source =>
            b with { Rows = a.Rows.AddRange(b.Rows), Complete = a.Complete && b.Complete },
        _ => next,
    };

    public static ToolPayload? TrimLast(ToolPayload? payload) => payload switch
    {
        AddonsPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        ValuesPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        NodesPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        MenuPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        SheetPayload p when p.Fields.Length > 0 => p with { Fields = p.Fields.RemoveAt(p.Fields.Length - 1) },
        InventoryPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        ComparisonPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        DomainPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        ProviderPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        NetworkPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        CallbackPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        DiagnosticFieldsPayload p when p.Fields.Length > 0 => p with { Fields = p.Fields.RemoveAt(p.Fields.Length - 1) },
        PluginInventoryPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        LookupPayload p when p.Rows.Length > 0 => p with { Rows = p.Rows.RemoveAt(p.Rows.Length - 1) },
        ObservationPayload p when p.Samples.Length > 0 => p with { Samples = p.Samples.RemoveAt(0), Dropped = p.Dropped + 1 },
        _ => null,
    };
}
