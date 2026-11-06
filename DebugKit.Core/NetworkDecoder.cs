using System.Collections.Immutable;
using System.Text.Json;

namespace DebugKit.Core;

/// <summary>Projection only: no raw response, seller identities, names or arbitrary server text retained.</summary>
public static class NetworkDecoder
{
    public static ToolStep Decode(Inspector inspector, ToolOptions options, ReadOnlyMemory<byte> bytes, DateTimeOffset retrieved)
    {
        if (!NetworkPolicy.Valid(inspector, options)) return new(null, Outcome: Outcome.Blocked, Reason: options.NetworkConsent ? Reason.InvalidRange : Reason.NetworkConsentRequired);
        if (bytes.Length > NetworkPolicy.MaximumResponseBytes) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.ResponseTooLarge);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            var rows = inspector == Inspector.MarketSummary ? Market(root, options, retrieved) : Housing(root, options, retrieved);
            return new(new NetworkPayload(inspector == Inspector.MarketSummary ? NetworkService.Universalis : NetworkService.PaissaDb,
                retrieved, rows), Examined: rows.Length, Matched: rows.Length);
        }
        catch (Exception) { return new(null, Outcome: Outcome.Unavailable, Reason: Reason.NetworkSchemaMismatch); }
    }

    private static ImmutableArray<EvidenceRow> Market(JsonElement root, ToolOptions o, DateTimeOffset now)
    {
        var results = root.GetProperty("results");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1) throw new JsonException();
        var item = results[0];
        if (item.GetProperty("itemId").GetUInt32() != o.ItemId) throw new JsonException();
        var scope = o.MarketScope == MarketScope.World ? "world" : "dc";
        var rows = ImmutableArray.CreateBuilder<EvidenceRow>();
        foreach (var quality in new[] { "nq", "hq" })
        {
            var value = item.GetProperty(quality);
            var min = Metric(value, "minListing", scope, "price");
            var median = Metric(value, "medianListing", scope, "price");
            var average = Metric(value, "averageSalePrice", scope, "price");
            var velocity = Metric(value, "dailySaleVelocity", scope, "quantity");
            rows.Add(new(quality, quality == "nq" ? "NQ cached summary" : "HQ cached summary",
                [DomainPolicies.F("MinimumListingPrice", min), DomainPolicies.F("MedianListingPrice", median),
                 DomainPolicies.F("AverageSalePrice (last 4 days)", average), DomainPolicies.F("DailySaleVelocity (last 4 days)", velocity)]));
        }
        var hasUploads = item.TryGetProperty("worldUploadTimes", out var uploads) && uploads.ValueKind != JsonValueKind.Null;
        if (hasUploads && (uploads.ValueKind != JsonValueKind.Array || uploads.GetArrayLength() > 128)) throw new JsonException();
        var stamps = new List<long>();
        if (hasUploads) foreach (var upload in uploads.EnumerateArray())
        {
            var world = upload.GetProperty("worldId").GetUInt32();
            if (world is 0 or > 65535) throw new JsonException();
            if (o.MarketScope == MarketScope.World && world != o.WorldId) continue;
            var stamp = upload.GetProperty("timestamp").GetInt64();
            if (stamp < 0 || stamp > 253402300799999) throw new JsonException();
            stamps.Add(stamp);
        }
        var oldest = stamps.Count == 0 ? (long?)null : stamps.Min();
        var latest = stamps.Count == 0 ? (long?)null : stamps.Max();
        rows.Add(new("freshness", o.MarketScope == MarketScope.World ? "Requested-world upload evidence (milliseconds since epoch)"
            : "Response upload evidence; may include worlds outside requested DC",
            [DomainPolicies.F("WorldUploadCount", hasUploads ? (int?)stamps.Count : null), DomainPolicies.F("OldestUploadEpochMs", oldest),
             DomainPolicies.F("NewestUploadEpochMs", latest), DomainPolicies.F("OldestUploadAgeSeconds", Age(oldest, now, true)),
             DomainPolicies.F("NewestUploadAgeSeconds", Age(latest, now, true))]));
        return rows.ToImmutable();
    }

    private static double? Metric(JsonElement root, string metric, string scope, string field)
    {
        if (!root.TryGetProperty(metric, out var outer)) return null;
        if (outer.ValueKind == JsonValueKind.Null) return null;
        if (!outer.TryGetProperty(scope, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        var number = value.GetProperty(field);
        if (number.ValueKind == JsonValueKind.Null) return null;
        var result = number.GetDouble();
        if (!double.IsFinite(result) || result < 0 || result > 1e15) throw new JsonException();
        return result;
    }

    private static ImmutableArray<EvidenceRow> Housing(JsonElement root, ToolOptions o, DateTimeOffset now)
    {
        if (root.GetProperty("id").GetInt32() != (int)o.District) throw new JsonException();
        var count = root.GetProperty("num_open_plots").GetInt32();
        var plots = root.GetProperty("open_plots");
        if (plots.ValueKind != JsonValueKind.Array || plots.GetArrayLength() > 1800 || count != plots.GetArrayLength()) throw new JsonException();
        double? oldest = null, newest = null;
        var missingStamps = 0;
        foreach (var plot in plots.EnumerateArray())
        {
            if (plot.GetProperty("world_id").GetUInt32() != o.WorldId || plot.GetProperty("district_id").GetInt32() != (int)o.District)
                throw new JsonException();
            if (!plot.TryGetProperty("last_updated_time", out var timestamp) || timestamp.ValueKind == JsonValueKind.Null)
            { missingStamps++; continue; }
            var stamp = timestamp.GetDouble();
            if (!double.IsFinite(stamp) || stamp < 0 || stamp > 253402300799) throw new JsonException();
            oldest = Math.Min(oldest ?? stamp, stamp); newest = Math.Max(newest ?? stamp, stamp);
        }
        if (missingStamps > 0) { oldest = null; newest = null; }
        return [new("district", "PaissaDB reported district summary",
            [DomainPolicies.F("ReportedOpenPlots (not purchase availability)", count),
             DomainPolicies.F("MissingObservationTimestamps", missingStamps),
             DomainPolicies.F("OldestPlotObservationEpochSeconds", oldest), DomainPolicies.F("NewestPlotObservationEpochSeconds", newest),
             DomainPolicies.F("OldestObservationAgeSeconds", Age(oldest, now, false)), DomainPolicies.F("NewestObservationAgeSeconds", Age(newest, now, false))])];
    }

    private static double? Age(double? epoch, DateTimeOffset now, bool milliseconds)
    {
        if (epoch is not > 0 || !double.IsFinite(epoch.Value)) return null;
        var age = now.ToUnixTimeMilliseconds() / 1000d - epoch.Value / (milliseconds ? 1000d : 1d);
        return age < 0 ? null : age;
    }
}
