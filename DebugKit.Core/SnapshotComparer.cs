using System.Collections.Immutable;
using System.Globalization;

namespace DebugKit.Core;

public static class SnapshotComparer
{
    public static ToolStep Compare(CaptureResult baseline, CaptureResult comparison, bool changesOnly)
    {
        if (baseline.RequestId == comparison.RequestId || baseline.Inspector != comparison.Inspector || baseline.SchemaVersion != comparison.SchemaVersion ||
            baseline.Inspector == Inspector.Compare || baseline.Options != comparison.Options ||
            baseline.ToolOptions != comparison.ToolOptions ||
            (baseline.Inspector != Inspector.SheetRow && !DomainPolicies.IsStatic(baseline.Inspector) && baseline.Generation != comparison.Generation) ||
            !CompatiblePayloads(baseline.Payload, comparison.Payload) ||
            baseline.Outcome != Outcome.Success || comparison.Outcome != Outcome.Success)
            return new(null, Outcome: Outcome.Blocked, Reason: Reason.IncompatibleSnapshots);

        var before = Entries(baseline).ToLookup(x => x.Key, x => x.Value);
        var after = Entries(comparison).ToLookup(x => x.Key, x => x.Value);
        var shortenedBefore = ShortenedKeys(baseline).ToHashSet(StringComparer.Ordinal);
        var shortenedAfter = ShortenedKeys(comparison).ToHashSet(StringComparer.Ordinal);
        var complete = !baseline.Partial && !comparison.Partial && !baseline.NonAtomic && !comparison.NonAtomic &&
            baseline.Inspector != Inspector.NearbyObjects && (baseline.Payload is not DomainPayload domain || domain.IdentityStable) &&
            shortenedBefore.Count == 0 && shortenedAfter.Count == 0;
        var rows = ImmutableArray.CreateBuilder<DifferenceRow>();
        var added = 0; var removed = 0; var changed = 0; var unchanged = 0; var unobserved = 0; var ambiguous = 0; var indeterminate = 0;
        var targetIdentityUnknown = baseline.Inspector == Inspector.Target;
        var allKeys = before.Select(x => x.Key).Union(after.Select(x => x.Key)).Order(StringComparer.Ordinal).ToArray();
        foreach (var key in allKeys.Take(256))
        {
            var a = before[key].ToArray(); var b = after[key].ToArray();
            ChangeKind change;
            if (a.Length > 1 || b.Length > 1) { change = ChangeKind.Ambiguous; ambiguous++; }
            else if (a.Length == 0) { change = complete ? ChangeKind.Added : ChangeKind.Unobserved; if (complete) added++; else unobserved++; }
            else if (b.Length == 0) { change = complete ? ChangeKind.Removed : ChangeKind.Unobserved; if (complete) removed++; else unobserved++; }
            else if (shortenedBefore.Contains(key) || shortenedAfter.Contains(key)) { change = ChangeKind.Indeterminate; indeterminate++; }
            else if (a[0] == b[0]) { change = ChangeKind.Unchanged; unchanged++; }
            else { change = ChangeKind.Changed; changed++; }
            if (!changesOnly || change != ChangeKind.Unchanged)
            {
                var beforeText = a.Length == 1 ? FieldText.Create("before", a[0]) : null;
                var afterText = b.Length == 1 ? FieldText.Create("after", b[0]) : null;
                rows.Add(new(ReportText.Clean(key), change, beforeText?.Value, afterText?.Value,
                    shortenedBefore.Contains(key) || beforeText?.TextShortened == true,
                    shortenedAfter.Contains(key) || afterText?.TextShortened == true));
            }
        }
        var limited = allKeys.Length > 256;
        return new(new ComparisonPayload(baseline.RequestId, comparison.RequestId, rows.ToImmutable(), added, removed,
            changed, unchanged, unobserved, ambiguous, complete && !targetIdentityUnknown && !limited, indeterminate),
            Partial: limited || !complete, Reason: limited ? Reason.RowLimit : Reason.None,
            Examined: Math.Min(256, allKeys.Length), Matched: rows.Count);
    }

    private static IEnumerable<string> ShortenedKeys(CaptureResult result) => result.Payload switch
    {
        SheetPayload p => p.Fields.Where(f => f.TextShortened).Select(f => f.Field),
        DomainPayload p => p.Rows.Where(r => r.Fields.Any(f => f.TextShortened)).Select(r => r.Key),
        _ => [],
    };

    private static bool CompatiblePayloads(ToolPayload? a, ToolPayload? b) => (a, b) switch
    {
        (ValuesPayload x, ValuesPayload y) => x.Lifetime == y.Lifetime && x.GameBuild == y.GameBuild,
        (NodesPayload x, NodesPayload y) => x.Lifetime == y.Lifetime && x.GameBuild == y.GameBuild,
        (MenuPayload x, MenuPayload y) => x.Lifetime == y.Lifetime && x.GameBuild == y.GameBuild,
        (SheetPayload x, SheetPayload y) => x.Language == y.Language && x.GameBuild != "Unavailable" && x.GameBuild == y.GameBuild,
        (InventoryPayload x, InventoryPayload y) => x.GameBuild == y.GameBuild,
        (AddonsPayload x, AddonsPayload y) => x.GameBuild == y.GameBuild && x.Profile == y.Profile,
        (DomainPayload x, DomainPayload y) => x.GameBuild != "Unavailable" && DomainPolicies.SameEvidence(x, y),
        (TargetPayload, TargetPayload) or (null, null) => true,
        _ => false,
    };

    private static IEnumerable<KeyValuePair<string, string>> Entries(CaptureResult result)
    {
        static KeyValuePair<string, string> Field(string key, object? value) => new(key,
            value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "unavailable");
        switch (result.Payload)
        {
            case TargetPayload p:
                yield return Field("Selected target observation (identity continuity unverified)", $"{p.Target.Object.Name}; {p.Target.Object.Kind}; base={p.Target.Object.BaseId}");
                yield return Field("HP", p.Target.Hp); yield return Field("MaxHP", p.Target.MaxHp);
                yield return Field("Position", p.Target.Object.Position); yield return Field("Targetable", p.Target.Object.Targetable);
                yield return Field("Job", p.Target.JobId); yield return Field("Level", p.Target.Level);
                yield return Field("Rotation", p.Target.Rotation); yield return Field("Hitbox", p.Target.HitboxRadius);
                break;
            case SheetPayload p:
                foreach (var f in p.Fields) yield return Field(f.Field, f.Value); break;
            case InventoryPayload p:
                foreach (var r in p.Rows) yield return Field($"{r.Bag}/slot {r.Slot}", $"item={r.ItemId}; HQ={r.Hq}; quantity={r.Quantity}; {r.Name}"); break;
            case AddonsPayload p:
                foreach (var r in p.Rows) yield return Field($"{r.Name}/{r.AddonId}", $"visible={r.Visible}; ready={r.Ready}; values={r.Values}; nodes={r.Nodes}"); break;
            case ValuesPayload p:
                foreach (var r in p.Rows) yield return Field($"value/{r.Index}", $"{r.Type}: {r.Value}"); break;
            case MenuPayload p:
                foreach (var r in p.Rows) yield return Field($"entry/{r.Index}", r.Value); break;
            case NodesPayload p:
                foreach (var r in p.Rows) yield return Field($"node/{r.NodeId}", $"type={r.Type}; depth={r.Depth}; visible={r.Visible}; x={r.X.ToString(CultureInfo.InvariantCulture)}; y={r.Y.ToString(CultureInfo.InvariantCulture)}; w={r.Width}; h={r.Height}"); break;
            case DomainPayload p:
                foreach (var r in p.Rows)
                    yield return Field(r.Key, r.Name + "; " + string.Join("; ", r.Fields.Select(f => $"{f.Field}={f.Value}")));
                break;
            default:
                if (result.Inspector == Inspector.NearbyObjects)
                    foreach (var r in result.Objects)
                        yield return Field($"observation/{r.Kind}/{r.BaseId}/{r.Name}", $"{r.Position}; targetable={r.Targetable}");
                else if (result.Context is { } c)
                {
                    yield return Field("LoggedIn", c.LoggedIn); yield return Field("Territory", c.TerritoryId);
                    yield return Field("PlayerAvailable", c.PlayerAvailable); yield return Field("Position", c.Position);
                    yield return Field("Job", c.JobId); yield return Field("Level", c.Level);
                    yield return Field("Combat", c.Conditions.InCombat); yield return Field("Casting", c.Conditions.Casting);
                    yield return Field("Mounted", c.Conditions.Mounted); yield return Field("Occupied", c.Conditions.Occupied);
                }
                break;
        }
    }
}
