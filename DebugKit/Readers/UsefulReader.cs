using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using DebugKit.Core;

namespace DebugKit.Readers;

/// <summary>Small allowlisted public API snapshots. Never reflects over game wrappers.</summary>
internal sealed class UsefulReader(IClientState client, IPlayerState player, IObjectTable objects,
    ICondition conditions, ITargetManager targets) : IObservationToolSource
{
    private long lastObserverSample;
    private Point? movementBaseline;
    private long movementBaselineTime;
    private readonly Queue<double> speedWindow = new();
    public ToolStep BigDump()
    {
        var rows = ImmutableArray.CreateBuilder<DiagnosticField>();
        Add(rows, "Client State", "LoggedIn", client.IsLoggedIn);
        Add(rows, "Client State", "TerritoryType", client.IsLoggedIn ? client.TerritoryType : null,
            client.IsLoggedIn ? FieldAvailability.Available : FieldAvailability.NotApplicable);
        var local = client.IsLoggedIn && !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51]
            ? objects.LocalPlayer : null;
        if (local == null || !player.IsLoaded || !local.IsValid())
        {
            Add(rows, "Player", "Name", null, FieldAvailability.Absent, "string");
            Add(rows, "Player", "Position", null, FieldAvailability.Absent, "Vector3");
        }
        else
        {
            Add(rows, "Player", "Name", ObjectNames.For(ObjectCategory.Player, local.Name.ToString()));
            Add(rows, "Player", "Kind", Enum.GetName(local.ObjectKind));
            var position = local.Position;
            Add(rows, "Player", "Position", Format($"<{position.X:F2}, {position.Y:F2}, {position.Z:F2}>"), FieldAvailability.Available, "Vector3");
            Add(rows, "Player", "Rotation", local.Rotation);
            Add(rows, "Player", "Targetable", local.IsTargetable);
            Add(rows, "Player", "Current HP", local.CurrentHp);
            Add(rows, "Player", "Maximum HP", local.MaxHp);
            Add(rows, "Player", "Current MP", local.CurrentMp);
            Add(rows, "Player", "Maximum MP", local.MaxMp);
            Add(rows, "Player", "ClassJob row", player.ClassJob.RowId);
            Add(rows, "Job", "Name", player.ClassJob.Value.Name.ExtractText());
            Add(rows, "Job", "Abbreviation", player.ClassJob.Value.Abbreviation.ExtractText());
            Add(rows, "Player", "Level", player.Level);
            Add(rows, "Worlds", "Current world ID", player.CurrentWorld.RowId);
            Add(rows, "Worlds", "Current world name", player.CurrentWorld.Value.Name.ExtractText());
            Add(rows, "Worlds", "Home world ID", player.HomeWorld.RowId);
            Add(rows, "Worlds", "Home world name", player.HomeWorld.Value.Name.ExtractText());
        }
        AddTarget(rows, "Selected target", targets.Target);
        AddTarget(rows, "Previous target", targets.PreviousTarget);
        AddTarget(rows, "Soft target", targets.SoftTarget);
        AddTarget(rows, "Mouseover target", targets.MouseOverTarget);
        AddTarget(rows, "Nameplate target", targets.MouseOverNameplateTarget);
        AddTarget(rows, "Focus target", targets.FocusTarget);
        AddTarget(rows, "GPose target", targets.GPoseTarget);
        return new(new DiagnosticFieldsPayload(rows.ToImmutable(), "Explicit public Dalamud fields; one framework-thread snapshot."),
            Examined: rows.Count, Matched: rows.Count);
    }

    public ToolStep ConditionFlags()
    {
        var rows = ImmutableArray.CreateBuilder<DiagnosticField>();
        foreach (var flag in Enum.GetValues<ConditionFlag>())
        {
            try
            {
                Add(rows, "Conditions", Enum.GetName(flag) ?? "Unknown",
                    $"value {(int)flag}; active {conditions[flag].ToString().ToLowerInvariant()}", FieldAvailability.Available, "ConditionFlag");
            }
            catch (Exception) { Add(rows, "Conditions", Enum.GetName(flag) ?? "Unknown", null, FieldAvailability.Failed, "bool"); }
        }
        return new(new DiagnosticFieldsPayload(rows.ToImmutable(), "Current SDK ConditionFlag enumeration."), Examined: rows.Count, Matched: rows.Count);
    }

    public ToolStep PlayerStatuses()
    {
        if (!client.IsLoggedIn || !player.IsLoaded || objects.LocalPlayer is not { } local || !local.IsValid())
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.PlayerUnavailable);
        if (local is not IBattleChara battle) return new(new DiagnosticFieldsPayload([
            new("Statuses", "Status list", "status collection", null, FieldAvailability.NotApplicable),
            new("Privacy", "Source object IDs", "identifier", null, FieldAvailability.Withheld),
        ], "Local player status snapshot."), Examined: 2);
        var rows = ImmutableArray.CreateBuilder<DiagnosticField>();
        var length = Math.Min(battle.StatusList.Length, 80);
        Add(rows, "Statuses", "Count", battle.StatusList.Length);
        for (var i = 0; i < length; i++)
        {
            try
            {
                var status = battle.StatusList[i];
                if (status == null) { Add(rows, "Statuses", $"[{i}]", null, FieldAvailability.Absent, "status"); continue; }
                var data = status.GameData.Value;
                Add(rows, "Statuses", $"[{i}] StatusId", status.StatusId);
                Add(rows, "Statuses", $"[{i}] Name", ObjectNames.For(ObjectCategory.EventNpc, data.Name.ExtractText()), FieldAvailability.Available, "string");
                Add(rows, "Statuses", $"[{i}] RemainingTime", status.RemainingTime);
            }
            catch (Exception)
            {
                Add(rows, "Statuses", $"[{i}]", null, FieldAvailability.Failed, "status");
            }
        }
        Add(rows, "Privacy", "Source object IDs", null, FieldAvailability.Withheld, "identifier");
        return new(new DiagnosticFieldsPayload(rows.ToImmutable(), "Local player status list; source IDs excluded."),
            Partial: battle.StatusList.Length > length, Reason: battle.StatusList.Length > length ? Reason.RowLimit : Reason.None,
            Examined: length, Matched: rows.Count);
    }

    public ToolStep SampleObservation(Inspector inspector, ToolOptions options)
    {
        var now = Stopwatch.GetTimestamp();
        if (lastObserverSample != 0 && Stopwatch.GetElapsedTime(lastObserverSample, now).TotalSeconds < options.ObservationInterval)
            return new(null, Complete: false);
        lastObserverSample = now;
        var rows = ImmutableArray.CreateBuilder<DiagnosticField>();
        if (!client.IsLoggedIn || conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
        {
            movementBaseline = null; movementBaselineTime = 0; speedWindow.Clear();
            Add(rows, "Observation", "State", "logged out or transitioning", FieldAvailability.Failed, "state");
            return new(new ObservationPayload([new(DateTimeOffset.UtcNow, rows.ToImmutable())], 0, inspector.ToString()),
                Complete: false, Examined: 1, Unavailable: 1);
        }
        switch (inspector)
        {
            case Inspector.MovementSpeedTracker:
                SampleMovement(options, now, rows);
                break;
            case Inspector.TargetDistanceTracker:
                SampleTargetDistance(rows);
                break;
            case Inspector.EntityNameWatch:
                SampleEntityNames(options, rows);
                break;
            default:
                return new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
        }
        var sample = new ObservationSample(DateTimeOffset.UtcNow, rows.ToImmutable());
        return new(new ObservationPayload([sample], 0, inspector.ToString()), Complete: false, Examined: 1, Matched: 1);
    }

    public void EndObservation(Inspector inspector)
    {
        lastObserverSample = 0;
        movementBaseline = null;
        movementBaselineTime = 0;
        speedWindow.Clear();
    }

    private void SampleMovement(ToolOptions options, long now, ImmutableArray<DiagnosticField>.Builder rows)
    {
        var local = client.IsLoggedIn && !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51]
            ? objects.LocalPlayer : null;
        if (local == null || !local.IsValid())
        {
            movementBaseline = null; movementBaselineTime = 0; speedWindow.Clear();
            Add(rows, "Movement", "Position", null, FieldAvailability.Absent, "Vector3");
            return;
        }
        var vector = local.Position;
        var position = new Point(vector.X, vector.Y, vector.Z);
        if (!position.IsFinite)
        {
            movementBaseline = null; movementBaselineTime = 0; speedWindow.Clear();
            Add(rows, "Movement", "Position", null, FieldAvailability.Failed, "Vector3");
            return;
        }
        Add(rows, "Movement", "Position", $"<{vector.X:F2}, {vector.Y:F2}, {vector.Z:F2}>", FieldAvailability.Available, "Vector3");
        Add(rows, "Movement", "Mounted", conditions[ConditionFlag.Mounted]);
        if (movementBaseline is not { } previous || movementBaselineTime == 0)
        {
            movementBaseline = position; movementBaselineTime = now;
            Add(rows, "Movement", "Speed", null, FieldAvailability.NotApplicable, "yalms/second");
            return;
        }
        var elapsed = Math.Max(Stopwatch.GetElapsedTime(movementBaselineTime, now).TotalSeconds, 0.000001);
        var dx = (double)position.X - previous.X; var dy = (double)position.Y - previous.Y; var dz = (double)position.Z - previous.Z;
        var flat = Math.Sqrt(dx * dx + dz * dz); var full = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        movementBaseline = position; movementBaselineTime = now;
        var discontinuity = full > 100;
        Add(rows, "Movement", "Elapsed seconds", elapsed);
        Add(rows, "Movement", "Flat displacement", flat);
        Add(rows, "Movement", "3D displacement", full);
        Add(rows, "Movement", "Discontinuity (>100y sampled displacement)", discontinuity);
        if (discontinuity) speedWindow.Clear();
        else
        {
            var speed = flat / elapsed;
            var moving = flat >= options.MinimumDistance;
            if (!options.OnlyWhenMoving || moving) speedWindow.Enqueue(speed);
            while (speedWindow.Count > options.RollingSamples) speedWindow.Dequeue();
            Add(rows, "Movement", "Flat speed (sampled)", speed);
            Add(rows, "Movement", "Meets minimum distance", moving);
            Add(rows, "Movement", "Rolling flat speed", speedWindow.Count == 0 ? null : speedWindow.Average(),
                speedWindow.Count == 0 ? FieldAvailability.NotApplicable : FieldAvailability.Available, "double");
        }
        Add(rows, "Movement", "3D speed (sampled)", discontinuity ? null : full / elapsed,
            discontinuity ? FieldAvailability.NotApplicable : FieldAvailability.Available, "double");
        Add(rows, "Movement", "Minimum-distance input", options.MinimumDistance);
    }

    private void SampleTargetDistance(ImmutableArray<DiagnosticField>.Builder rows)
    {
        var local = objects.LocalPlayer;
        var target = targets.Target;
        if (local == null || !local.IsValid() || target == null || !target.IsValid())
        {
            Add(rows, "Target", "State", target == null ? "No target" : "Player or target unavailable",
                target == null ? FieldAvailability.Absent : FieldAvailability.Failed, "state");
            return;
        }
        var a = local.Position; var b = target.Position;
        var flatX = (double)a.X - b.X; var flatZ = (double)a.Z - b.Z; var dy = (double)a.Y - b.Y;
        Add(rows, "Target", "Name", ObjectNames.For(ObjectCategory.Other, target.Name.ToString()), FieldAvailability.Available, "string");
        Add(rows, "Target", "Flat XZ distance", Math.Sqrt(flatX * flatX + flatZ * flatZ));
        Add(rows, "Target", "3D distance", Math.Sqrt(flatX * flatX + flatZ * flatZ + dy * dy));
        Add(rows, "Target", "Distance meaning", "origin-to-origin; not hitbox-adjusted interaction range", FieldAvailability.Available, "note");
    }

    private void SampleEntityNames(ToolOptions options, ImmutableArray<DiagnosticField>.Builder rows)
    {
        var local = objects.LocalPlayer;
        if (!client.IsLoggedIn || local == null || !local.IsValid())
        { Add(rows, "Entity scan", "State", "Player unavailable", FieldAvailability.Failed, "state"); return; }
        var origin = local.Position;
        var length = Math.Min(objects.Length, 1024);
        var examined = 0; var matches = 0; var unavailable = 0;
        for (var i = 0; i < length && matches < 16; i++)
        {
            examined++;
            try
            {
                var obj = objects[i];
                if (obj == null || !obj.IsValid()) continue;
                var name = ObjectNames.For(ObjectCategory.Other, obj.Name.ToString());
                if (name == null || (options.ExactName
                    ? !name.Equals(options.ObservationName, StringComparison.OrdinalIgnoreCase)
                    : !name.Contains(options.ObservationName, StringComparison.OrdinalIgnoreCase))) continue;
                var p = obj.Position;
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) continue;
                matches++;
                Add(rows, "Entity scan", $"Match {matches}", $"{name} | <{p.X:F2}, {p.Y:F2}, {p.Z:F2}>", FieldAvailability.Available, "name and position");
            }
            catch (Exception) { unavailable++; }
        }
        Add(rows, "Entity scan", "Name filter", options.ObservationName);
        Add(rows, "Entity scan", "Match mode", options.ExactName ? "exact" : "substring");
        Add(rows, "Entity scan", "Matches", matches);
        Add(rows, "Entity scan", "Entries examined", examined);
        Add(rows, "Entity scan", "Unreadable entries", unavailable);
        Add(rows, "Entity scan", "Scan complete", length == objects.Length && examined == length && matches < 16 && unavailable == 0);
        Add(rows, "Entity scan", "Interpretation", "not observed does not prove despawn; observed again does not prove respawn or identity continuity", FieldAvailability.Available, "limitation");
    }

    private static void AddTarget(ImmutableArray<DiagnosticField>.Builder rows, string section, IGameObject? target)
    {
        if (target == null || !target.IsValid())
        {
            Add(rows, section, "Object", null, FieldAvailability.Absent, "game object");
            return;
        }
        var kind = target is IPlayerCharacter ? ObjectCategory.Player : ObjectCategory.Other;
        Add(rows, section, "Name", ObjectNames.For(kind, target.Name.ToString()));
        Add(rows, section, "Kind", Enum.GetName(target.ObjectKind));
        var p = target.Position;
        Add(rows, section, "Position", Format($"<{p.X:F2}, {p.Y:F2}, {p.Z:F2}>"), FieldAvailability.Available, "Vector3");
        Add(rows, section, "Rotation", target.Rotation);
        Add(rows, section, "Targetable", target.IsTargetable);
        if (target is ICharacter character)
        {
            Add(rows, section, "Current HP", character.CurrentHp);
            Add(rows, section, "Maximum HP", character.MaxHp);
            Add(rows, section, "Current MP", character.CurrentMp);
            Add(rows, section, "Maximum MP", character.MaxMp);
            Add(rows, section, "ClassJob row", character.ClassJob.RowId);
            Add(rows, section, "Level", character.Level);
        }
        else Add(rows, section, "Character fields", null, FieldAvailability.NotApplicable, "character");
    }

    private static void Add(ImmutableArray<DiagnosticField>.Builder rows, string section, string field,
        object? value, FieldAvailability state = FieldAvailability.Available, string? type = null)
    {
        var inferred = type ?? value?.GetType().Name ?? "unknown";
        var text = value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        rows.Add(new(section, field, inferred, text, state));
    }

    private static string Format(string value) => value;
}
