using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DebugKit.Readers.Native;

/// <summary>Reviewed native read boundary. Every pointer/iterator is local to one framework call.</summary>
internal sealed unsafe class NativeReader : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private readonly IAddonLifecycle lifecycle;
    private readonly RuntimeCompatibility compatibility;
    private readonly SheetReader sheets;
    private long epoch;
    private bool disposed, tracking;

    public NativeReader(IFramework framework, IGameGui gui, IAddonLifecycle lifecycle,
        RuntimeCompatibility compatibility, SheetReader sheets)
    {
        this.framework = framework; this.gui = gui; this.lifecycle = lifecycle;
        this.compatibility = compatibility; this.sheets = sheets;
    }

    private void OnLifetime(AddonEvent _, AddonArgs args) => Interlocked.Increment(ref epoch);

    public ToolStep Capture(Inspector inspector, ToolOptions options, int cursor, long? expectedStamp)
    {
        if (disposed || !framework.IsInFrameworkUpdateThread) throw new InvalidOperationException();
        if (!compatibility.Supported) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.UnsupportedBuild);
        if (!options.ExperimentalNative) return new(null, Outcome: Outcome.Blocked, Reason: Reason.NativeOptInRequired);
        if (!tracking)
        {
            lifecycle.RegisterListener(AddonEvent.PostSetup, OnLifetime);
            try { lifecycle.RegisterListener(AddonEvent.PreFinalize, OnLifetime); tracking = true; }
            catch { lifecycle.UnregisterListener(OnLifetime); throw; }
        }
        var stamp = Volatile.Read(ref epoch);
        if (expectedStamp is { } expected && expected != stamp)
            return new(null, Outcome: Outcome.Cancelled, Reason: Reason.SourceChanged, Partial: true, SourceStamp: stamp);
        var result = inspector switch
        {
            Inspector.AddonList => Addons(options, cursor, stamp),
            Inspector.AddonValues => Values(options, stamp),
            Inspector.AddonNodes => Nodes(options, stamp),
            Inspector.ContextMenu => Menu(options, stamp),
            Inspector.Inventory => Inventory(options, cursor),
            Inspector.Currency => Currency(options, cursor),
            _ => new ToolStep(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable),
        };
        if (inspector is not (Inspector.Inventory or Inspector.Currency) && Volatile.Read(ref epoch) != stamp)
            return new(null, Outcome: Outcome.Cancelled, Reason: Reason.SourceChanged, Partial: true, SourceStamp: Volatile.Read(ref epoch));
        return result;
    }

    private ToolStep Addons(ToolOptions options, int cursor, long stamp)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
        var list = options.Scope == AddonScope.Focused ? &manager->AtkUnitManager.FocusedUnitsList : &manager->AtkUnitManager.AllLoadedUnitsList;
        var length = Math.Min((int)list->Count, list->Entries.Length);
        var rows = ImmutableArray.CreateBuilder<AddonRow>();
        var examined = 0; var empty = 0; var filtered = 0;
        var start = Stopwatch.GetTimestamp();
        while (cursor < length && examined < 32)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 2) break;
            var addon = list->Entries[cursor++].Value; examined++;
            if (addon == null) { empty++; continue; }
            var name = ReportText.Clean(addon->NameString);
            if ((options.Scope == AddonScope.Visible && !addon->IsVisible) ||
                (options.Addon.Length > 0 && !name.Contains(options.Addon, StringComparison.OrdinalIgnoreCase)))
            { filtered++; continue; }
            rows.Add(new(name, addon->Id, addon->IsVisible, addon->IsReady,
                addon->IsReady ? addon->AtkValuesCount : null, addon->IsReady ? addon->UldManager.NodeListCount : null));
        }
        var truncated = list->Count > list->Entries.Length;
        return new(new AddonsPayload(rows.ToImmutable(), compatibility.GameBuild, compatibility.Status), cursor,
            cursor >= length, truncated, Reason: truncated ? Reason.EntryLimit : Reason.None, SourceStamp: stamp,
            Examined: examined, Matched: rows.Count, Empty: empty, Filtered: filtered);
    }

    private AtkUnitBase* Find(ToolOptions options)
    {
        var wrapper = gui.GetAddonByName(options.Addon, options.Instance);
        return wrapper.IsNull ? null : (AtkUnitBase*)wrapper.Address;
    }

    private static ToolStep? Validate(AtkUnitBase* addon)
    {
        if (addon == null) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.AddonUnavailable);
        if (!addon->IsReady || !addon->IsVisible) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.AddonUnready);
        return null;
    }

    private ToolStep Values(ToolOptions options, long stamp)
    {
        var addon = Find(options);
        if (Validate(addon) is { } error) return error;
        if (options.Start >= addon->AtkValuesCount || addon->AtkValues == null)
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.InvalidRange);
        var end = Math.Min(options.Start + options.Count, addon->AtkValuesCount);
        var rows = ImmutableArray.CreateBuilder<ValueRow>();
        var tick = Stopwatch.GetTimestamp();
        for (var index = options.Start; index < end; index++)
        {
            if (rows.Count > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var value = addon->AtkValues[index];
            var type = ReportText.Clean(Enum.GetName(value.Type) ?? "Unknown");
            var decoded = value.Type == AtkValueType.Bool;
            var text = decoded ? value.Bool.ToString() : "omitted (unclassified field/type; no pointer dereference)";
            // Reviewed ContextMenu count is a small public UI count, not an identity/address.
            if (options.Addon == "ContextMenu" && index == 0 && value.Type == AtkValueType.UInt && value.UInt <= 64)
            { text = value.UInt.ToString(CultureInfo.InvariantCulture); decoded = true; }
            rows.Add(new(index, type, text, decoded));
        }
        var partial = rows.Count < options.Count;
        return new(new ValuesPayload(options.Addon, options.Instance, stamp, rows.ToImmutable(), compatibility.GameBuild),
            Partial: partial, Reason: partial ? Reason.EntryLimit : Reason.None, SourceStamp: stamp,
            Examined: rows.Count, Matched: rows.Count);
    }

    private ToolStep Nodes(ToolOptions options, long stamp)
    {
        var addon = Find(options);
        if (Validate(addon) is { } error) return error;
        var list = addon->UldManager.NodeList;
        var total = addon->UldManager.NodeListCount;
        if (total > addon->UldManager.NodeListSize) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.InvalidRange);
        if (list == null && total > 0) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
        var rows = ImmutableArray.CreateBuilder<NodeRow>();
        var visited = new TraversalGuard<nint>();
        var examined = 0; var invalid = 0; var empty = 0; var filtered = 0; var limited = total > 256;
        var tick = Stopwatch.GetTimestamp();
        for (var index = 0; index < Math.Min((int)total, 256); index++)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) { limited = true; break; }
            var node = list[index]; examined++;
            if (node == null) { empty++; continue; }
            if (!visited.TryVisit((nint)node, 0)) { invalid++; limited = true; continue; }
            if (options.NodeId is { } id && id != node->NodeId) { filtered++; continue; }
            var chain = new TraversalGuard<nint>(options.Depth, 9);
            chain.TryVisit((nint)node, 0);
            var parent = node->ParentNode; var depth = 0; var depthLimited = false;
            while (parent != null)
            {
                if (!chain.TryVisit((nint)parent, depth + 1)) { depthLimited = true; break; }
                depth++; parent = parent->ParentNode;
            }
            if (parent != null) depthLimited = true;
            if (!float.IsFinite(node->X) || !float.IsFinite(node->Y)) { invalid++; limited = true; continue; }
            limited |= depthLimited;
            rows.Add(new(index, node->NodeId, (ushort)node->Type, depth, node->IsVisible(), node->X, node->Y, node->Width, node->Height, depthLimited));
        }
        return new(new NodesPayload(options.Addon, options.Instance, stamp, rows.ToImmutable(), compatibility.GameBuild),
            Partial: limited, Reason: limited ? Reason.EntryLimit : Reason.None, SourceStamp: stamp,
            Examined: examined, Matched: rows.Count, Empty: empty, Invalid: invalid, Filtered: filtered);
    }

    private ToolStep Menu(ToolOptions options, long stamp)
    {
        var addon = Find(options with { Addon = "ContextMenu" });
        if (Validate(addon) is { } error) return error;
        if (addon->AtkValues == null || addon->AtkValuesCount < 8 || addon->AtkValues[0].Type != AtkValueType.UInt)
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.UnsupportedBuild);
        var count = addon->AtkValues[0].UInt;
        if (count > addon->AtkValuesCount - 8 || count > 64)
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.UnsupportedBuild);
        var rows = ImmutableArray.CreateBuilder<ValueRow>();
        var tick = Stopwatch.GetTimestamp();
        for (var index = 0; index < count; index++)
        {
            if (index > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var value = addon->AtkValues[8 + index];
            var text = value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString
                ? ReadString(value.String.Value) : null;
            rows.Add(new(index, ReportText.Clean(Enum.GetName(value.Type)), text ?? "unavailable (malformed, unsupported, or bounded SeString)", text != null));
        }
        var partial = rows.Count < count || rows.Any(r => !r.Decoded);
        return new(new MenuPayload(options.Instance, stamp, rows.ToImmutable(), compatibility.GameBuild),
            Partial: partial, SourceStamp: stamp, Examined: rows.Count, Matched: rows.Count);
    }

    private static string? ReadString(byte* pointer)
    {
        if (pointer == null) return null;
        var count = 0;
        while (count < MenuLabelDecoder.MaxBytes && pointer[count] != 0) count++;
        return count == MenuLabelDecoder.MaxBytes ? null : MenuLabelDecoder.Decode(new ReadOnlySpan<byte>(pointer, count));
    }

    private ToolStep Inventory(ToolOptions options, int cursor)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ContainerUnavailable);
        var first = options.Bag == InventoryBag.All ? 0 : (int)options.Bag - 1;
        var last = options.Bag == InventoryBag.All ? 3 : first;
        if (cursor == 0) cursor = first * 256;
        var rows = ImmutableArray.CreateBuilder<InventoryRow>();
        var examined = 0; var empty = 0; var invalid = 0; var filtered = 0; var unavailable = 0;
        var tick = Stopwatch.GetTimestamp();
        for (var operations = 0; operations < 32 && cursor / 256 <= last; operations++)
        {
            if (operations > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var bag = cursor / 256; var slot = cursor % 256;
            var container = manager->GetInventoryContainer((InventoryType)((int)InventoryType.Inventory1 + bag));
            if (container == null || !InventoryPolicy.Ready(container->IsLoaded, container->Items != null, container->Size))
            { unavailable++; cursor = (bag + 1) * 256; continue; }
            if (slot >= container->Size) { cursor = (bag + 1) * 256; continue; }
            var item = &container->Items[slot]; cursor++; examined++;
            if (item->IsSymbolic || item->Quantity < 0) { invalid++; continue; }
            var id = item->ItemId;
            if (id == 0)
            {
                empty++;
                if (options.IncludeEmpty && options.ItemId == null && options.Quality == ItemQuality.Any)
                    rows.Add(new((InventoryBag)(bag + 1), slot, 0, null, 0, false));
                continue;
            }
            var hq = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
            if (!InventoryPolicy.Matches(options, id, hq))
            { filtered++; continue; }
            rows.Add(new((InventoryBag)(bag + 1), slot, id, sheets.ItemName(id), item->Quantity, hq));
        }
        var complete = cursor / 256 > last;
        return new(new InventoryPayload(rows.ToImmutable(), compatibility.GameBuild), cursor, complete,
            unavailable > 0 || invalid > 0, unavailable > 0 ? Outcome.Unavailable : Outcome.Success,
            unavailable > 0 ? Reason.ContainerUnavailable : Reason.None,
            Examined: examined, Matched: rows.Count, Empty: empty, Invalid: invalid, Filtered: filtered, Unavailable: unavailable);
    }

    private ToolStep Currency(ToolOptions options, int cursor)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ContainerUnavailable);
        var container = manager->GetInventoryContainer(InventoryType.Currency);
        if (container == null || !InventoryPolicy.Ready(container->IsLoaded, container->Items != null, container->Size))
            return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ContainerUnavailable);
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        var examined = 0; var empty = 0; var invalid = 0; var filtered = 0;
        var start = Stopwatch.GetTimestamp();
        while (cursor < container->Size && examined < 32)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 2) break;
            var item = &container->Items[cursor++]; examined++;
            if (item->IsSymbolic || item->Quantity < 0) { invalid++; continue; }
            if (item->ItemId == 0) { empty++; continue; }
            if (item->ItemId != options.ItemId) { filtered++; continue; }
            rows.Add(new($"currency/{item->ItemId}", sheets.ItemName(item->ItemId) ?? "unavailable",
                [DomainPolicies.F("ItemId", item->ItemId), DomainPolicies.F("ObservedQuantity", item->Quantity), DomainPolicies.F("Cap", null)]));
        }
        return new(new DomainPayload(Inspector.Currency, "Reviewed Currency inventory container", sheets.ClientLanguageName, compatibility.GameBuild,
            null, null, true, rows.ToImmutable(), "Currency container only, not all special currencies. Missing row is not zero; caps/unlocks unavailable."),
            cursor, cursor >= container->Size, invalid > 0, Examined: examined, Matched: rows.Count, Empty: empty, Invalid: invalid, Filtered: filtered);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (tracking) lifecycle.UnregisterListener(OnLifetime);
    }
}
