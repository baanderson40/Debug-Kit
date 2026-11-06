using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DebugKit.Core;

public enum WorldAsset { Planevent, Planmap }
public enum PlacementKind { All, EventObject, EventRange, BattleNpc, EventNpc, Treasure }
public sealed record EvidenceRow(string Key, string Name, ImmutableArray<FieldRow> Fields);
public sealed record DomainPayload(Inspector Kind, string Source, string Language, string GameBuild, uint? Territory,
    long? Lifetime, bool IdentityStable, ImmutableArray<EvidenceRow> Rows, string Limitations, Point? FilterOrigin = null) : ToolPayload;
public sealed record SchedulePhase(long Cycle, string Phase, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public static class DomainPolicies
{
    public static bool IsDomain(Inspector inspector) => inspector is Inspector.Fates or Inspector.Casts or Inspector.BozjaEvents or
        Inspector.OccultEvents or Inspector.WorldPlacements or Inspector.CollisionBounds or Inspector.FamiliarData or Inspector.FamiliarUi or
        Inspector.HousingSchedule or Inspector.HousingUi or Inspector.Duty or Inspector.Currency or Inspector.RetainerTimers or Inspector.ProcessInfo;
    public static bool NeedsNative(Inspector inspector) => inspector is Inspector.Currency or Inspector.FamiliarUi or Inspector.HousingUi;
    public static bool IsStatic(Inspector inspector) => inspector is Inspector.FamiliarData or Inspector.Duty or Inspector.RetainerTimers or Inspector.HousingSchedule or Inspector.ProcessInfo;
    public static bool TerritorySupported(Inspector inspector, uint territory) => inspector switch
    {
        Inspector.BozjaEvents => territory is 920 or 975,
        Inspector.OccultEvents => territory == 1252,
        _ => true,
    };
    public static bool Valid(Inspector inspector, ToolOptions o) => inspector switch
    {
        Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents => Radius(o.DomainRadius) && Id(o.FilterId) && Name(o.NameFilter) && (o.StateFilter == null || o.StateFilter is >= 0 and <= 255),
        Inspector.Casts => Radius(o.DomainRadius) && Id(o.FilterId) && Name(o.NameFilter),
        Inspector.WorldPlacements or Inspector.CollisionBounds => Radius(o.DomainRadius) && Id(o.FilterId) && Enum.IsDefined(o.Asset) && Enum.IsDefined(o.Placement),
        Inspector.FamiliarData => o.Sheet is SheetKind.XBMPet or SheetKind.XBMItem or SheetKind.XBMContent && Enum.IsDefined(o.Language),
        Inspector.Duty => Enum.IsDefined(o.Language),
        Inspector.RetainerTimers => Enum.IsDefined(o.Language) && (o.RemainingSeconds == null || o.RemainingSeconds is >= 0 and <= 604800),
        Inspector.Currency => o.ItemId is > 0,
        Inspector.HousingSchedule => o.EntryDays is >= 1 and <= 30 && o.ResultsDays is >= 1 and <= 30 &&
            (o.ScheduleEpoch == null || o.ScheduleEpoch.Value.Offset == TimeSpan.Zero),
        Inspector.FamiliarUi or Inspector.HousingUi or Inspector.ProcessInfo => true,
        _ => false,
    };
    public static ToolOptions Canonical(Inspector inspector, ToolOptions o) => inspector switch
    {
        Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents => new(DomainRadius: o.DomainRadius, FilterId: o.FilterId, NameFilter: o.NameFilter, StateFilter: o.StateFilter),
        Inspector.Casts => new(DomainRadius: o.DomainRadius, FilterId: o.FilterId, NameFilter: o.NameFilter, SelectedOnly: o.SelectedOnly),
        Inspector.WorldPlacements => new(DomainRadius: o.DomainRadius, FilterId: o.FilterId, Asset: o.Asset, Placement: o.Placement),
        Inspector.CollisionBounds => new(DomainRadius: o.DomainRadius, Asset: o.Asset),
        Inspector.FamiliarData => new(Sheet: o.Sheet, RowId: o.RowId, Language: o.Language),
        Inspector.Duty => new(Sheet: SheetKind.ContentFinderCondition, RowId: o.RowId, Language: o.Language),
        Inspector.RetainerTimers => new(Sheet: SheetKind.RetainerTask, RowId: o.RowId, Language: o.Language, RemainingSeconds: o.RemainingSeconds),
        Inspector.Currency => new(ItemId: o.ItemId, ExperimentalNative: o.ExperimentalNative),
        Inspector.HousingSchedule => new(ScheduleEpoch: o.ScheduleEpoch, EntryDays: o.EntryDays, ResultsDays: o.ResultsDays),
        Inspector.FamiliarUi or Inspector.HousingUi => new(ExperimentalNative: o.ExperimentalNative),
        _ => new(),
    };
    private static bool Radius(float radius) => float.IsFinite(radius) && radius is >= 1 and <= 200;
    private static bool Id(uint? id) => id != 0;
    private static bool Name(string? name) => name is { Length: <= 64 } && name.All(c => !char.IsControl(c) && c != '#');
    public static bool Matches(ToolOptions options, uint id, string? name, double distance) =>
        double.IsFinite(distance) && distance >= 0 && distance <= options.DomainRadius &&
        (options.FilterId == null || options.FilterId == id) &&
        (options.NameFilter.Length == 0 || name?.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase) == true);
    public static bool SameEvidence(DomainPayload a, DomainPayload b) => a.Kind == b.Kind && a.Source == b.Source &&
        a.Language == b.Language && a.GameBuild == b.GameBuild && a.Territory == b.Territory &&
        a.Lifetime == b.Lifetime && a.IdentityStable == b.IdentityStable && a.Limitations == b.Limitations && a.FilterOrigin == b.FilterOrigin;
    public static string? AssetPath(string? bg, WorldAsset asset)
    {
        if (bg is not { Length: >= 1 and <= 256 } || !Enum.IsDefined(asset) || bg.StartsWith('/') ||
            !Regex.IsMatch(bg, @"\A[A-Za-z0-9_/]+\z", RegexOptions.CultureInvariant)) return null;
        var last = bg.LastIndexOf('/');
        if (last < 0) return null;
        return "bg/" + bg[..(last + 1)] + (asset == WorldAsset.Planevent ? "planevent.lgb" : "planmap.lgb");
    }
    public static FieldRow F(string name, object? value) => FieldText.Create(name, value is IFormattable f
        ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString());
    public static string Position(Point p) => FormattableString.Invariant($"<{p.X:R}, {p.Y:R}, {p.Z:R}>");

    public static SchedulePhase? Schedule(DateTimeOffset now, DateTimeOffset epoch, int entryDays, int resultDays)
    {
        if (entryDays is < 1 or > 30 || resultDays is < 1 or > 30 || now < epoch) return null;
        try
        {
            var cycleTicks = TimeSpan.FromDays(entryDays + resultDays).Ticks;
            var cycle = (now.UtcTicks - epoch.UtcTicks) / cycleTicks;
            var start = epoch.ToUniversalTime().AddTicks(cycle * cycleTicks);
            var entryEnd = start.AddDays(entryDays);
            return now < entryEnd ? new(cycle, "Entry (calculated)", start, entryEnd)
                : new(cycle, "Results (calculated)", entryEnd, start.AddTicks(cycleTicks));
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
