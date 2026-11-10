using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using DebugKit.Readers;
using DebugKit.Readers.Native;

namespace DebugKit;

/// <summary>Public API reader including requested display names, but no object IDs or native traversal.</summary>
internal sealed class DalamudGameSource(IFramework framework, IClientState client, IPlayerState player,
    IObjectTable objects, ICondition conditions, ITargetManager targets, SheetReader sheets, NativeReader native, DomainReader domains,
    ProviderReader providers, NetworkCaptureClient network, AddonCallbackObserver callbacks) : IGameSource, IToolSource, IAsyncToolSource, ICallbackToolSource, IObservationToolSource
{
    private readonly UsefulReader useful = new(client, player, objects, conditions, targets);
    private void RequireFrameworkThread()
    {
        if (!framework.IsInFrameworkUpdateThread) throw new InvalidOperationException();
    }

    public Task<ToolStep> StartNetworkCapture(Inspector inspector, ToolOptions options, CancellationToken cancellationToken)
    {
        RequireFrameworkThread();
        // Frozen public inputs only. HTTP/JSON work never reads game wrappers or provider state.
        return Task.Run(() => network.CaptureAsync(inspector, options, cancellationToken));
    }

    public Task<ToolStep> StartCallbackCapture(ToolOptions options, CancellationToken cancellationToken)
    {
        RequireFrameworkThread();
        if (!client.IsLoggedIn) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Blocked, Reason: Reason.LoggedOut));
        if (conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
            return Task.FromResult(new ToolStep(null, Outcome: Outcome.Blocked, Reason: Reason.Loading));
        if (!player.IsLoaded || objects.LocalPlayer is not { } local || !local.IsValid())
            return Task.FromResult(new ToolStep(null, Outcome: Outcome.Unavailable, Reason: Reason.PlayerUnavailable));
        return callbacks.Start(options, cancellationToken);
    }

    public void RequestCallbackStop() => callbacks.RequestCallbackStop();

    public LiveContext ReadContext()
    {
        RequireFrameworkThread();
        var loggedIn = client.IsLoggedIn;
        domains.Context(loggedIn ? client.TerritoryType : 0);
        var flags = new Conditions(conditions[ConditionFlag.InCombat], conditions[ConditionFlag.Casting],
            conditions[ConditionFlag.Mounted], conditions[ConditionFlag.Occupied],
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51]);
        var local = loggedIn && !flags.BetweenAreas ? objects.LocalPlayer : null;
        var ready = local != null && local.IsValid() && player.IsLoaded;
        Point? position = ready ? new(local!.Position.X, local.Position.Y, local.Position.Z) : null;
        return new(new(loggedIn, loggedIn ? client.TerritoryType : null, ready, position,
            ready ? player.ClassJob.RowId : null, ready ? player.Level : null, flags),
            loggedIn && player.IsLoaded ? player.ContentId : 0);
    }

    public int ObjectTableLength
    {
        get { RequireFrameworkThread(); return objects.Length; }
    }

    public ObjectSample? ReadObject(int index)
    {
        RequireFrameworkThread();
        if (index < 0 || index >= objects.Length) throw new InvalidOperationException();
        var obj = objects[index];
        if (obj == null) return null;
        if (!obj.IsValid()) return new(ObjectCategory.Other, null, default, false, false);
        var nativeKind = obj.ObjectKind;
        var kind = obj is IPlayerCharacter ? ObjectCategory.Player : nativeKind switch
        {
            ObjectKind.BattleNpc => ObjectCategory.BattleNpc,
            ObjectKind.EventNpc => ObjectCategory.EventNpc,
            ObjectKind.EventObj => ObjectCategory.EventObject,
            ObjectKind.Treasure => ObjectCategory.Treasure,
            ObjectKind.Companion => ObjectCategory.Companion,
            _ => ObjectCategory.Other,
        };
        var pos = obj.Position;
        return new(kind, kind is ObjectCategory.Player or ObjectCategory.Other ? null : obj.BaseId,
            new(pos.X, pos.Y, pos.Z), obj.IsTargetable, true,
            (byte)nativeKind, NativeKinds.Label(Enum.GetName(nativeKind)), ReadDisplayName(obj, kind));
    }

    private static string? ReadDisplayName(IGameObject obj, ObjectCategory kind)
    {
        // Missing/unreadable names don't discard otherwise useful object rows.
        try
        {
            return ObjectNames.For(kind, obj.Name.ToString());
        }
        catch (Exception) { return null; }
    }

    public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? expectedStamp)
    {
        RequireFrameworkThread();
        if (inspector == Inspector.BigDump)
        {
            if (!client.IsLoggedIn) return new(null, Outcome: Outcome.Blocked, Reason: Reason.LoggedOut);
            if (conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
                return new(null, Outcome: Outcome.Blocked, Reason: Reason.Loading);
            return useful.BigDump();
        }
        if (inspector == Inspector.ConditionFlags) return useful.ConditionFlags();
        if (inspector == Inspector.PlayerStatuses) return useful.PlayerStatuses();
        if (inspector == Inspector.InstalledPlugins) return providers.InstalledPlugins();
        if (inspector == Inspector.JobLookup) return options.ListJobs
            ? sheets.CaptureJobList(options)
            : sheets.Capture(options with { Sheet = SheetKind.ClassJob });
        if (inspector == Inspector.NpcNameLookup && options.Sheet is SheetKind.BNpcName or SheetKind.ENpcResident)
            return sheets.Capture(options);
        if (inspector == Inspector.TerritoryLookup) return sheets.Capture(options with { Sheet = SheetKind.TerritoryType });
        if (inspector == Inspector.SheetRow) return sheets.Capture(options);
        if (inspector == Inspector.ProviderMetadata) return providers.Metadata();
        if (inspector == Inspector.ProviderReadiness) return providers.Query(options.Provider);
        if (inspector == Inspector.Target) return ReadTarget();
        if (DomainPolicies.IsStatic(inspector)) return domains.Capture(inspector, options, cursor, expectedStamp);
        if (!client.IsLoggedIn) return new(null, Outcome: Outcome.Blocked, Reason: Reason.LoggedOut);
        if (conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
            return new(null, Outcome: Outcome.Blocked, Reason: Reason.Loading);
        if (DomainPolicies.IsDomain(inspector) && Catalog.Get(inspector).RequiresPlayer &&
            (!player.IsLoaded || objects.LocalPlayer is not { } local || !local.IsValid()))
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.PlayerUnavailable);
        return DomainPolicies.IsDomain(inspector) ? domains.Capture(inspector, options, cursor, expectedStamp)
            : native.Capture(inspector, options, cursor, expectedStamp);
    }

    public ToolStep SampleObservation(Inspector inspector, ToolOptions options)
    {
        RequireFrameworkThread();
        return useful.SampleObservation(inspector, options);
    }

    public void EndObservation(Inspector inspector)
    {
        RequireFrameworkThread();
        useful.EndObservation(inspector);
    }

    private ToolStep ReadTarget()
    {
        if (!client.IsLoggedIn) return new(null, Outcome: Outcome.Blocked, Reason: Reason.LoggedOut);
        if (conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
            return new(null, Outcome: Outcome.Blocked, Reason: Reason.Loading);
        var target = targets.Target;
        if (target == null || !target.IsValid()) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.NoTarget);
        var nativeKind = target.ObjectKind;
        var kind = target is IPlayerCharacter ? ObjectCategory.Player : nativeKind switch
        {
            ObjectKind.BattleNpc => ObjectCategory.BattleNpc, ObjectKind.EventNpc => ObjectCategory.EventNpc,
            ObjectKind.EventObj => ObjectCategory.EventObject, ObjectKind.Treasure => ObjectCategory.Treasure,
            ObjectKind.Companion => ObjectCategory.Companion, _ => ObjectCategory.Other,
        };
        var pos = target.Position;
        var position = new Point(pos.X, pos.Y, pos.Z);
        if (!position.IsFinite) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ObjectReadFailed);
        var local = objects.LocalPlayer;
        double? distance = local == null ? null : position.Distance(new(local.Position.X, local.Position.Y, local.Position.Z));
        var row = new ObjectRow(kind, kind is ObjectCategory.Player or ObjectCategory.Other ? null : target.BaseId,
            position, distance, target.IsTargetable, (byte)nativeKind, NativeKinds.Label(Enum.GetName(nativeKind)), ReadDisplayName(target, kind));
        var character = target as ICharacter;
        var (statusAvailability, statuses, statusCount, statusesTruncated, statusReadFailed) = ReadTargetStatuses(target);
        var targetData = new TargetData(row, float.IsFinite(target.Rotation) ? target.Rotation : null,
            float.IsFinite(target.HitboxRadius) ? target.HitboxRadius : null, character?.CurrentHp, character?.MaxHp,
            character?.ClassJob.RowId, character?.Level, statusAvailability, statuses, statusCount, statusesTruncated);
        return new(new TargetPayload(targetData), Partial: statusReadFailed || statusesTruncated,
            Reason: statusReadFailed ? Reason.ObjectReadFailed : statusesTruncated ? Reason.RowLimit : Reason.None,
            Examined: 1, Matched: 1);
    }

    private static (TargetStatusAvailability Availability, System.Collections.Immutable.ImmutableArray<TargetStatusData> Statuses,
        int? Count, bool Truncated, bool ReadFailed) ReadTargetStatuses(IGameObject target)
    {
        if (target is not IBattleChara battle)
            return (TargetStatusAvailability.Unsupported, [], null, false, false);

        try
        {
            var list = battle.StatusList;
            var count = list.Length;
            if (count == 0) return (TargetStatusAvailability.Empty, [], 0, false, false);
            var rows = System.Collections.Immutable.ImmutableArray.CreateBuilder<TargetStatusData>(Math.Min(count, 80));
            var failed = false;
            for (var i = 0; i < Math.Min(count, 80); i++)
            {
                try
                {
                    var status = list[i];
                    if (status == null)
                    {
                        rows.Add(new(null, null, null, true));
                        failed = true;
                        continue;
                    }
                    var data = status.GameData.Value;
                    var remaining = status.RemainingTime;
                    rows.Add(new(status.StatusId, data.Name.ExtractText(), float.IsFinite(remaining) ? remaining : null));
                }
                catch (Exception)
                {
                    rows.Add(new(null, null, null, true));
                    failed = true;
                }
            }
            return (failed ? TargetStatusAvailability.Failed : TargetStatusAvailability.Available,
                rows.ToImmutable(), count, count > 80, failed);
        }
        catch (Exception)
        {
            return (TargetStatusAvailability.Failed, [], null, false, true);
        }
    }
}
