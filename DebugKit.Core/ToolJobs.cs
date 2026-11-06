namespace DebugKit.Core;

/// <summary>Explicit tool registration, independent of catalog/enum ordering. No reflection or batch dispatch.</summary>
public static class ToolJobs
{
    private static readonly IReadOnlyDictionary<Inspector, Func<IToolSource, ToolOptions, int, long?, ToolStep>> Readers =
        new Dictionary<Inspector, Func<IToolSource, ToolOptions, int, long?, ToolStep>>
        {
            [Inspector.Target] = (s, o, c, e) => s.ReadToolStep(Inspector.Target, o, c, e),
            [Inspector.ProviderMetadata] = (s, o, c, e) => s.ReadToolStep(Inspector.ProviderMetadata, o, c, e),
            [Inspector.BigDump] = (s, o, c, e) => s.ReadToolStep(Inspector.BigDump, o, c, e),
            [Inspector.ConditionFlags] = (s, o, c, e) => s.ReadToolStep(Inspector.ConditionFlags, o, c, e),
            [Inspector.PlayerStatuses] = (s, o, c, e) => s.ReadToolStep(Inspector.PlayerStatuses, o, c, e),
            [Inspector.InstalledPlugins] = (s, o, c, e) => s.ReadToolStep(Inspector.InstalledPlugins, o, c, e),
            [Inspector.JobLookup] = (s, o, c, e) => s.ReadToolStep(Inspector.JobLookup, o, c, e),
            [Inspector.NpcNameLookup] = (s, o, c, e) => s.ReadToolStep(Inspector.NpcNameLookup, o, c, e),
            [Inspector.TerritoryLookup] = (s, o, c, e) => s.ReadToolStep(Inspector.TerritoryLookup, o, c, e),
            [Inspector.MovementSpeedTracker] = (s, o, c, e) => s.ReadToolStep(Inspector.MovementSpeedTracker, o, c, e),
            [Inspector.TargetDistanceTracker] = (s, o, c, e) => s.ReadToolStep(Inspector.TargetDistanceTracker, o, c, e),
            [Inspector.EntityNameWatch] = (s, o, c, e) => s.ReadToolStep(Inspector.EntityNameWatch, o, c, e),
            [Inspector.ProviderReadiness] = (s, o, c, e) => s.ReadToolStep(Inspector.ProviderReadiness, o, c, e),
            [Inspector.AddonList] = (s, o, c, e) => s.ReadToolStep(Inspector.AddonList, o, c, e),
            [Inspector.AddonValues] = (s, o, c, e) => s.ReadToolStep(Inspector.AddonValues, o, c, e),
            [Inspector.AddonNodes] = (s, o, c, e) => s.ReadToolStep(Inspector.AddonNodes, o, c, e),
            [Inspector.ContextMenu] = (s, o, c, e) => s.ReadToolStep(Inspector.ContextMenu, o, c, e),
            [Inspector.SheetRow] = (s, o, c, e) => s.ReadToolStep(Inspector.SheetRow, o, c, e),
            [Inspector.Inventory] = (s, o, c, e) => s.ReadToolStep(Inspector.Inventory, o, c, e),
            [Inspector.Fates] = (s, o, c, e) => s.ReadToolStep(Inspector.Fates, o, c, e),
            [Inspector.Casts] = (s, o, c, e) => s.ReadToolStep(Inspector.Casts, o, c, e),
            [Inspector.BozjaEvents] = (s, o, c, e) => s.ReadToolStep(Inspector.BozjaEvents, o, c, e),
            [Inspector.OccultEvents] = (s, o, c, e) => s.ReadToolStep(Inspector.OccultEvents, o, c, e),
            [Inspector.WorldPlacements] = (s, o, c, e) => s.ReadToolStep(Inspector.WorldPlacements, o, c, e),
            [Inspector.CollisionBounds] = (s, o, c, e) => s.ReadToolStep(Inspector.CollisionBounds, o, c, e),
            [Inspector.FamiliarData] = (s, o, c, e) => s.ReadToolStep(Inspector.FamiliarData, o, c, e),
            [Inspector.FamiliarUi] = (s, o, c, e) => s.ReadToolStep(Inspector.FamiliarUi, o, c, e),
            [Inspector.HousingSchedule] = (s, o, c, e) => s.ReadToolStep(Inspector.HousingSchedule, o, c, e),
            [Inspector.HousingUi] = (s, o, c, e) => s.ReadToolStep(Inspector.HousingUi, o, c, e),
            [Inspector.Duty] = (s, o, c, e) => s.ReadToolStep(Inspector.Duty, o, c, e),
            [Inspector.Currency] = (s, o, c, e) => s.ReadToolStep(Inspector.Currency, o, c, e),
            [Inspector.RetainerTimers] = (s, o, c, e) => s.ReadToolStep(Inspector.RetainerTimers, o, c, e),
            [Inspector.ProcessInfo] = (s, o, c, e) => s.ReadToolStep(Inspector.ProcessInfo, o, c, e),
        };

    public static ToolStep Step(Inspector inspector, ToolOptions options, int cursor, long? stamp,
        IToolSource? source, Func<Guid, CaptureResult?> lookup)
    {
        if (inspector == Inspector.Compare)
        {
            var baseline = lookup(options.Baseline!.Value);
            var comparison = lookup(options.Comparison!.Value);
            return baseline == null || comparison == null
                ? new(null, Outcome: Outcome.Unavailable, Reason: Reason.SnapshotUnavailable)
                : SnapshotComparer.Compare(baseline, comparison, options.ChangesOnly);
        }
        return source != null && Readers.TryGetValue(inspector, out var reader)
            ? reader(source, options, cursor, stamp)
            : new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
    }
}
