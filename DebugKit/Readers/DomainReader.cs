using System.Collections.Immutable;
using System.Diagnostics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using DebugKit.Readers.Native;
using static DebugKit.Core.DomainPolicies;

namespace DebugKit.Readers;

/// <summary>Framework-only public API/data readers; no native pointers or other-plugin state.</summary>
internal sealed class DomainReader(IFramework framework, IClientState client, IObjectTable objects, ITargetManager targets,
    IFateTable fates, IDataManager data, RuntimeCompatibility compatibility, SheetReader sheets, NativeReader native)
{
    private readonly WorldReader world = new(data, compatibility);
    private Point captureOrigin; // Detached scalar; engine serializes captures and every new job starts at cursor zero.

    public void Context(uint territory)
    {
        world.Context(territory);
        if (territory == 0) captureOrigin = default;
    }

    public ToolStep Capture(Inspector inspector, ToolOptions options, int cursor, long? stamp)
    {
        if (!framework.IsInFrameworkUpdateThread) throw new InvalidOperationException();
        if (inspector is Inspector.FamiliarData or Inspector.Duty or Inspector.RetainerTimers) return StaticSheet(inspector, options);
        if (inspector == Inspector.HousingSchedule) return HousingSchedule(options);
        if (inspector == Inspector.ProcessInfo) return ProcessMemory();
        if (!TerritorySupported(inspector, client.TerritoryType)) return new(null, Outcome: Outcome.Blocked, Reason: Reason.UnsupportedContext);
        if (inspector is Inspector.FamiliarUi or Inspector.HousingUi) return UiMetadata(inspector, options, cursor, stamp);
        if (inspector == Inspector.Currency) return native.Capture(inspector, options, cursor, stamp);
        var local = objects.LocalPlayer;
        if (local == null || !local.IsValid()) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.PlayerUnavailable);
        var p = local.Position;
        var position = new Point(p.X, p.Y, p.Z);
        if (!position.IsFinite) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.PlayerUnavailable);
        if (cursor == 0) captureOrigin = position;
        var origin = captureOrigin;
        return inspector switch
        {
            Inspector.Fates or Inspector.BozjaEvents or Inspector.OccultEvents => Fates(inspector, options, cursor, stamp, origin),
            Inspector.Casts => Casts(options, cursor, stamp, origin),
            Inspector.WorldPlacements or Inspector.CollisionBounds => world.Capture(inspector, options, cursor, origin, client.TerritoryType),
            _ => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable),
        };
    }

    private DomainPayload Payload(Inspector inspector, string source, ImmutableArray<EvidenceRow> rows, string limitations,
        bool stable = false, string? language = null, uint? territory = null, long? lifetime = null, Point? origin = null) =>
        new(inspector, source, language ?? data.Language.ToString(), compatibility.GameBuild, territory, lifetime, stable, rows, limitations, origin);

    private ToolStep Fates(Inspector inspector, ToolOptions o, int cursor, long? expected, Point origin)
    {
        var length = fates.Length;
        if (length < 0) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
        if (expected != null && expected != length) return new(null, Outcome: Outcome.Cancelled, Reason: Reason.SourceChanged, Partial: true);
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        var examined = 0; var empty = 0; var invalid = 0; var filtered = 0; var unavailable = 0;
        var tick = Stopwatch.GetTimestamp();
        while (cursor < length && examined < 32)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var index = cursor++; examined++;
            try
            {
                var fate = fates[index];
                if (fate == null) { empty++; continue; }
                if (!fates.IsValid(fate)) { invalid++; continue; }
                var pos = fate.Position; var position = new Point(pos.X, pos.Y, pos.Z);
                if (!position.IsFinite || !float.IsFinite(fate.Radius) || fate.Radius < 0 || fate.Progress > 100) { invalid++; continue; }
                var name = ObjectNames.For(ObjectCategory.EventObject, fate.Name.TextValue);
                var distance = position.Distance(origin);
                var state = (int)fate.State;
                if (!Matches(o, fate.FateId, name, distance) || (o.StateFilter is { } filter && filter != state)) { filtered++; continue; }
                rows.Add(new($"fate/{fate.FateId}", name ?? "unavailable", [F("RowId", fate.FateId), F("State", state),
                    F("ProgressPercent", fate.Progress), F("Level", fate.Level), F("Position", Position(position)), F("Distance", distance),
                    F("Radius", fate.Radius), F("StartEpochSeconds", fate.StartTimeEpoch > 0 ? fate.StartTimeEpoch : null),
                    F("DurationSeconds", fate.Duration > 0 ? fate.Duration : null),
                    F("RemainingSeconds (observed)", fate.StartTimeEpoch > 0 && fate.Duration > 0 ? Math.Max(0, fate.TimeRemaining) : null)]));
            }
            catch (Exception) { unavailable++; }
        }
        var complete = cursor >= length;
        return new(Payload(inspector, "Public IFateTable", rows.ToImmutable(), inspector == Inspector.Fates
                ? "FATE observations; row equality does not prove event-instance continuity."
                : "FATE evidence only; CE runtime state/associations deferred, not inferred.", territory: client.TerritoryType, origin: origin),
            cursor, complete, unavailable > 0 || invalid > 0, SourceStamp: length, Examined: examined, Matched: rows.Count,
            Empty: empty, Invalid: invalid, Filtered: filtered, Unavailable: unavailable,
            Outcome: unavailable > 0 ? Outcome.Unavailable : Outcome.Success, Reason: unavailable > 0 ? Reason.SourceUnavailable : Reason.None);
    }

    private ToolStep Casts(ToolOptions o, int cursor, long? expected, Point origin)
    {
        var length = o.SelectedOnly ? 1 : objects.Length;
        if (expected != null && expected != length) return new(null, Outcome: Outcome.Cancelled, Reason: Reason.SourceChanged, Partial: true);
        if (o.SelectedOnly && targets.Target == null) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.NoTarget);
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        var examined = 0; var empty = 0; var invalid = 0; var filtered = 0; var unavailable = 0;
        var tick = Stopwatch.GetTimestamp();
        while (cursor < length && examined < 32)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var index = cursor++; examined++;
            try
            {
                var obj = o.SelectedOnly ? targets.Target : objects[index];
                if (obj == null) { empty++; continue; }
                if (!obj.IsValid()) { invalid++; continue; }
                if (obj is not IBattleChara battle || !battle.IsCasting) { filtered++; continue; }
                var p = obj.Position; var point = new Point(p.X, p.Y, p.Z);
                if (!point.IsFinite || !float.IsFinite(battle.CurrentCastTime) || !float.IsFinite(battle.TotalCastTime) ||
                    battle.CurrentCastTime < 0 || battle.TotalCastTime < 0) { invalid++; continue; }
                var name = ObjectNames.For(ObjectCategory.Other, obj.Name.TextValue);
                var distance = point.Distance(origin); var action = battle.CastActionId; var type = battle.CastActionType;
                if (!Matches(o, action, name, distance)) { filtered++; continue; }
                // Observation key only, not an entity identifier. Same-name casts remain ambiguous.
                rows.Add(new($"cast/{action}/{name}", name ?? "unavailable", [F("ObjectKind", (byte)obj.ObjectKind), F("ActionId", action),
                    F("ActionType", type), F("ActionName", sheets.ActionName(action, type)), F("CurrentSeconds", battle.CurrentCastTime),
                    F("TotalSeconds", battle.TotalCastTime), F("Interruptible", battle.IsCastInterruptible), F("Position", Position(point)), F("Distance", distance)]));
            }
            catch (Exception) { unavailable++; }
        }
        return new(Payload(Inspector.Casts, "Public IBattleChara", rows.ToImmutable(), "Observed casts, not action availability or entity continuity; cast target IDs omitted.", territory: client.TerritoryType, origin: origin),
            cursor, cursor >= length, unavailable > 0 || invalid > 0, SourceStamp: length,
            Examined: examined, Matched: rows.Count, Empty: empty, Invalid: invalid, Filtered: filtered, Unavailable: unavailable,
            Outcome: unavailable > 0 ? Outcome.Unavailable : Outcome.Success, Reason: unavailable > 0 ? Reason.SourceUnavailable : Reason.None);
    }

    private ToolStep StaticSheet(Inspector inspector, ToolOptions options)
    {
        var step = sheets.Capture(options);
        if (step.Payload is not SheetPayload sheet) return step;
        var fields = sheet.Fields;
        var limitations = inspector switch
        {
            Inspector.Duty => "Static requirements only; unlock, rewards, currency mapping, average item level and launch readiness unavailable.",
            Inspector.FamiliarData => "Raw schema column labels preserved. No inferred stats, roster ownership, stage semantics or card choices.",
            _ => "Static task maximum is not live venture time. No retainer identity or provider state; optional timer is user supplied.",
        };
        if (inspector == Inspector.RetainerTimers && options.RemainingSeconds is { } seconds)
            fields = fields.Add(F("UserSuppliedRemainingSeconds", seconds)).Add(F("EstimatedCompletionUtc (calculated)", DateTimeOffset.UtcNow.AddSeconds(seconds).ToString("O")));
        if (inspector == Inspector.Duty) fields = fields.Add(F("PartySize (interpreted)", null)).Add(F("UnlockState", null)).Add(F("LaunchReadiness", null));
        var row = new EvidenceRow($"{sheet.Sheet}/{sheet.RowId}", $"{sheet.Sheet} row {sheet.RowId}", fields);
        return step with { Payload = Payload(inspector, "Installed typed sheets", [row], limitations, true, sheet.Language) };
    }

    private ToolStep HousingSchedule(ToolOptions o)
    {
        if (o.ScheduleEpoch is not { } epoch || Schedule(DateTimeOffset.UtcNow, epoch, o.EntryDays, o.ResultsDays) is not { } phase)
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ScheduleUndefined);
        return new(Payload(Inspector.HousingSchedule, "User-supplied schedule calculation", [new("schedule", phase.Phase,
            [F("EpochUtc (supplied)", epoch.ToString("O")), F("EntryDays (supplied)", o.EntryDays), F("ResultsDays (supplied)", o.ResultsDays),
             F("Cycle (zero-based)", phase.Cycle), F("PhaseStartUtc", phase.StartUtc.ToString("O")), F("PhaseEndUtc (exclusive)", phase.EndUtc.ToString("O"))])],
            "Calculated from supplied rules, not observed lottery/bid state or an authoritative game schedule.", true, "Not applicable"), Examined: 1, Matched: 1);
    }

    private ToolStep ProcessMemory()
    {
        using var process = Process.GetCurrentProcess();
        return new(Payload(Inspector.ProcessInfo, "Current process public metrics", [new("process-memory", "Current process",
            [F("WorkingSetBytes", process.WorkingSet64), F("PrivateBytes", process.PrivateMemorySize64), F("ManagedHeapEstimateBytes", GC.GetTotalMemory(false)),
             F("Gen0Collections", GC.CollectionCount(0)), F("Gen1Collections", GC.CollectionCount(1)), F("Gen2Collections", GC.CollectionCount(2))])],
            "Point-in-time process metrics, not attribution to Debug Kit or a frame-performance guarantee. Window/render controls deferred.", true, "Not applicable"), Examined: 1, Matched: 1);
    }

    private ToolStep UiMetadata(Inspector inspector, ToolOptions options, int cursor, long? expected)
    {
        var step = native.Capture(Inspector.AddonList, new(Addon: "", Scope: AddonScope.Loaded, ExperimentalNative: options.ExperimentalNative), cursor, expected);
        if (step.Payload is not AddonsPayload addons) return step;
        var rows = addons.Rows.Where(r => inspector == Inspector.FamiliarUi ? r.Name.StartsWith("XBM", StringComparison.Ordinal) : r.Name.StartsWith("Housing", StringComparison.Ordinal))
            .Select(r => new EvidenceRow($"addon/{r.Name}/{r.AddonId}", r.Name,
                [F("AddonId (not instance index)", r.AddonId), F("Visible", r.Visible), F("Ready", r.Ready), F("Values", r.Values), F("Nodes", r.Nodes)])).ToImmutableArray();
        return step with { Payload = Payload(inspector, "Reviewed addon-list metadata", rows,
            "Metadata only: semantic roster/stage/cards/bids/timers are deferred. No addon text/private state decoded.",
            territory: client.TerritoryType, lifetime: step.SourceStamp), Matched = rows.Length, Filtered = step.Filtered + addons.Rows.Length - rows.Length };
    }
}
