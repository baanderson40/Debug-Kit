namespace DebugKit.Core;

/// <summary>Selection labels describe immutable capture inputs, never the current UI form.</summary>
public static class SnapshotLabels
{
    public static string For(CaptureResult result)
    {
        var inputs = Inputs(result);
        return FormattableString.Invariant($"{Catalog.Get(result.Inspector).Title}{(inputs.Length == 0 ? "" : " · " + inputs)} · {result.CompletedUtc:HH:mm:ss} UTC · {result.Outcome} · {result.RequestId.ToString()[..8]}");
    }

    public static string Inputs(CaptureResult result)
    {
        if (result.Inspector == Inspector.NearbyObjects && result.Options is { } nearby)
            return FormattableString.Invariant($"radius {nearby.Radius:0.##}y · {nearby.Kind} · {(nearby.BaseId is { } id ? $"base {id}" : "all base IDs")}");
        if (result.ToolOptions is not { } options) return "";
        return result.Inspector switch
        {
            Inspector.Target => "selected target",
            Inspector.AddonCallbacks => $"{CallbackNames.Scope(options.Addon)} · manual start/stop · rolling 64-event buffer · integers may contain IDs",
            Inspector.AddonList => $"{options.Scope} · {(options.Addon.Length == 0 ? "all names" : $"filter \"{options.Addon}\"")}",
            Inspector.AddonValues => FormattableString.Invariant($"{options.Addon} [{options.Instance}] · start {options.Start} · count {options.Count}"),
            Inspector.AddonNodes => FormattableString.Invariant($"{options.Addon} [{options.Instance}] · {(options.NodeId is { } node ? $"node {node}" : "all nodes")} · depth {options.Depth}"),
            Inspector.ContextMenu => FormattableString.Invariant($"ContextMenu [{options.Instance}]"),
            Inspector.SheetRow => FormattableString.Invariant($"{options.Sheet} #{options.RowId} · {options.Language}"),
            Inspector.JobLookup => options.ListJobs ? $"ClassJob IDs 1–128 · {options.Language}" : FormattableString.Invariant($"ClassJob #{options.RowId} · {options.Language}"),
            Inspector.NpcNameLookup => FormattableString.Invariant($"{options.Sheet} #{options.RowId} · {options.Language} · direct lookup only"),
            Inspector.TerritoryLookup => FormattableString.Invariant($"TerritoryType #{options.RowId} · {options.Language}"),
            Inspector.MovementSpeedTracker => FormattableString.Invariant($"manual start/stop · {options.ObservationInterval:0.##}s · min {options.MinimumDistance:0.##}y · rolling {options.RollingSamples}"),
            Inspector.TargetDistanceTracker => FormattableString.Invariant($"manual start/stop · {options.ObservationInterval:0.##}s · origin distance"),
            Inspector.EntityNameWatch => FormattableString.Invariant($"manual start/stop · {options.ObservationInterval:0.##}s · {(options.ExactName ? "exact" : "substring")} \"{options.ObservationName}\""),
            Inspector.InstalledPlugins => "public inventory; plugin names and versions",
            Inspector.Inventory => $"{options.Bag} · {(options.ItemId is { } item ? $"item {item}" : "all items")} · {options.Quality}{(options.IncludeEmpty ? " · include empty" : "")}",
            Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents => FormattableString.Invariant($"{options.DomainRadius:0.##}y · ID {options.FilterId?.ToString() ?? "any"} · name \"{options.NameFilter}\" · state {options.StateFilter?.ToString() ?? "any"}"),
            Inspector.Casts => FormattableString.Invariant($"{options.DomainRadius:0.##}y · action {options.FilterId?.ToString() ?? "any"} · name \"{options.NameFilter}\" · selected {options.SelectedOnly}"),
            Inspector.WorldPlacements => FormattableString.Invariant($"{options.Asset} · {options.Placement} · {options.DomainRadius:0.##}y · base {options.FilterId?.ToString() ?? "any"}"),
            Inspector.CollisionBounds => FormattableString.Invariant($"{options.Asset} · CollisionBox · origin within {options.DomainRadius:0.##}y"),
            Inspector.FamiliarData or Inspector.Duty => $"{options.Sheet} #{options.RowId} · {options.Language}",
            Inspector.RetainerTimers => $"task #{options.RowId} · {options.Language} · supplied seconds {options.RemainingSeconds?.ToString() ?? "none"}",
            Inspector.Currency => $"item {options.ItemId}",
            Inspector.HousingSchedule => $"epoch {options.ScheduleEpoch?.ToString("O") ?? "unset"} · entry {options.EntryDays}d · results {options.ResultsDays}d",
            Inspector.FamiliarUi or Inspector.HousingUi => "allowlisted loaded addon metadata",
            Inspector.ProcessInfo => "current process only",
            Inspector.ProviderMetadata => "fixed provider catalog; metadata only",
            Inspector.ProviderReadiness => options.Provider.ToString(),
            Inspector.MarketSummary => FormattableString.Invariant($"item {options.ItemId} · {(options.MarketScope == MarketScope.World ? $"world {options.WorldId}" : $"DC {options.DataCenter}")} · explicit HTTPS"),
            Inspector.HousingService => FormattableString.Invariant($"world {options.WorldId} · {options.District} · explicit HTTPS"),
            _ => "",
        };
    }
}
