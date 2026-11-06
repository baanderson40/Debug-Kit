using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using Lumina.Data;
using Lumina.Excel.Sheets;
using static DebugKit.Core.DomainPolicies;

namespace DebugKit.Readers;

/// <summary>Installed static assets only. One bounded detached cache; never native runtime layouts.</summary>
internal sealed class WorldReader(IDataManager data, RuntimeCompatibility compatibility)
{
    private uint territory;
    private WorldAsset? cachedAsset;
    private StaticLayout? cached;
    private string cachedHash = "";
    public void Context(uint current)
    {
        if (territory == current) return;
        territory = current; cachedAsset = null; cached = null; cachedHash = "";
    }

    public ToolStep Capture(Inspector inspector, ToolOptions o, int cursor, Point origin, uint current)
    {
        Context(current);
        if (cached == null || cachedAsset != o.Asset)
        {
            cached = null; cachedAsset = null; cachedHash = "";
            if (!data.GetExcelSheet<TerritoryType>().TryGetRow(current, out var row)) return Missing();
            var path = AssetPath(row.Bg.ExtractText(), o.Asset);
            if (path == null) return Missing();
            // Get only the raw resource: Lumina's full LGB parser allocates arrays from file counts before our bounds.
            var raw = data.GetFile<FileResource>(path);
            if (raw == null || StaticLayoutParser.Parse(raw.Data) is not { } parsed) return Missing();
            cached = parsed; cachedAsset = o.Asset;
            cachedHash = Convert.ToHexString(SHA256.HashData(raw.Data)).ToLowerInvariant();
        }
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        var examined = 0; var filtered = 0;
        var tick = Stopwatch.GetTimestamp();
        while (cursor < cached.Entries.Length && examined < 32)
        {
            if (examined > 0 && Stopwatch.GetElapsedTime(tick).TotalMilliseconds >= 2) break;
            var entry = cached.Entries[cursor++]; examined++;
            if (inspector == Inspector.CollisionBounds ? entry.Type != "CollisionBox" : !Supported(o.Placement, entry.Type)) { filtered++; continue; }
            var distance = entry.Position.Distance(origin);
            if (distance > o.DomainRadius || (o.FilterId != null && o.FilterId != entry.BaseId)) { filtered++; continue; }
            var fields = ImmutableArray.Create(F("Type", entry.Type), F("BaseId", entry.BaseId), F("Position", Position(entry.Position)),
                F("Rotation (raw asset)", Position(entry.Rotation)), F("Scale (raw asset)", Position(entry.Scale)), F("DistanceToOrigin", distance), F("LayerIndex", entry.Layer));
            if (entry.Shape != null) fields = fields.Add(F("Shape (raw)", entry.Shape)).Add(F("Enabled (static)", entry.Enabled));
            rows.Add(new($"{o.Asset}/layer/{entry.Layer}/entry/{entry.Entry}", entry.Type, fields));
        }
        var complete = cursor >= cached.Entries.Length;
        return new(new DomainPayload(inspector, $"Installed {o.Asset} LGB1 SHA256 {cachedHash}", "Not applicable", compatibility.GameBuild, current, null, true,
            rows.ToImmutable(), "Static placements/transforms, not loaded/spawned runtime state. Collision scale is not a proven AABB or navigation result.", origin),
            cursor, complete, cached.Partial, Reason: cached.Partial ? Reason.EntryLimit : Reason.None, SourceStamp: current,
            Examined: examined, Matched: rows.Count, Filtered: filtered);
    }

    private static ToolStep Missing() => new(null, Outcome: Outcome.Unavailable, Reason: Reason.AssetUnavailable);
    private static bool Supported(PlacementKind filter, string type) => filter switch
    {
        PlacementKind.All => type is "EventObject" or "EventRange" or "BattleNPC" or "EventNPC" or "Treasure",
        PlacementKind.BattleNpc => type == "BattleNPC", PlacementKind.EventNpc => type == "EventNPC",
        _ => type == filter.ToString(),
    };
}
