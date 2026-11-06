using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace DebugKit.Core;

public enum PerformanceArea { FrameworkStep, ContextRead, ReaderJob, TextFormatting, JsonSerialization, PreviewPreparation, WindowDraw }
public sealed record PerformanceSummary(Inspector? Inspector, PerformanceArea Area, long Samples, int WarmSamples,
    double FirstMs, long FirstBytes, double MedianMs, double P95Ms, double MaxMs, long MedianBytes, long P95Bytes, long MaxBytes);

/// <summary>Opt-in, session-only instrumentation. No game data or unbounded sample history.</summary>
public sealed class PerformanceRecorder
{
    public const int WarmSampleLimit = 256;
    private readonly object gate = new();
    private readonly Dictionary<(Inspector?, PerformanceArea), Series> series = new();
    private bool enabled;
    private long epoch;
    private readonly record struct Sample(double Ms, long Bytes);
    private sealed class Series(Sample first)
    {
        public Sample First { get; } = first;
        public long Count = 1;
        public readonly Queue<Sample> Warm = new();
    }

    public bool Enabled
    {
        get { lock (gate) return enabled; }
        set { lock (gate) { if (enabled != value) { enabled = value; epoch++; } } }
    }

    public Scope Measure(Inspector? inspector, PerformanceArea area)
    {
        lock (gate) return enabled && Enum.IsDefined(area) && (inspector == null || Enum.IsDefined(inspector.Value))
            ? new(this, inspector, area, epoch) : default;
    }

    public void Clear() { lock (gate) { series.Clear(); epoch++; } }

    public ImmutableArray<PerformanceSummary> Snapshot()
    {
        lock (gate)
        {
            return series.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2).Select(x =>
            {
                var times = x.Value.Warm.Select(s => s.Ms).Order().ToArray();
                var allocations = x.Value.Warm.Select(s => s.Bytes).Order().ToArray();
                return new PerformanceSummary(x.Key.Item1, x.Key.Item2, x.Value.Count, times.Length,
                    x.Value.First.Ms, x.Value.First.Bytes, Percentile(times, .5), Percentile(times, .95),
                    times.LastOrDefault(), Percentile(allocations, .5), Percentile(allocations, .95), allocations.LastOrDefault());
            }).ToImmutableArray();
        }
    }

    public static string Describe(ImmutableArray<PerformanceSummary> rows)
    {
        var text = new StringBuilder("Session profiling: synchronous wall ms / current-thread managed allocation bytes. Nested scopes overlap. First measured is not proof of cold load. Warm statistics use latest <=256 later operations.\n");
        if (rows.IsEmpty) text.AppendLine("No samples. Enable collection, then run explicit captures/previews.");
        foreach (var row in rows)
        {
            text.AppendLine(FormattableString.Invariant($"{(row.Inspector is { } inspector ? Catalog.Get(inspector).Title : "All / idle")} · {row.Area} · measured {row.Samples} · warm retained {row.WarmSamples}"));
            text.AppendLine(FormattableString.Invariant($"  First {row.FirstMs:F3}ms / {row.FirstBytes} B"));
            if (row.WarmSamples == 0) text.AppendLine("  Warm median / p95 / max: unavailable (no later samples)");
            else
            {
                text.AppendLine(FormattableString.Invariant($"  Warm ms median {row.MedianMs:F3} / p95 {row.P95Ms:F3} / max {row.MaxMs:F3}"));
                text.AppendLine(FormattableString.Invariant($"  Warm B median {row.MedianBytes} / p95 {row.P95Bytes} / max {row.MaxBytes}"));
            }
        }
        return text.ToString();
    }

    private static T Percentile<T>(T[] sorted, double percentile) where T : struct =>
        sorted.Length == 0 ? default : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private void Record(Inspector? inspector, PerformanceArea area, long capturedEpoch, double milliseconds, long allocated)
    {
        lock (gate)
        {
            if (!enabled || epoch != capturedEpoch) return;
            var sample = new Sample(milliseconds, Math.Max(0, allocated));
            if (!series.TryGetValue((inspector, area), out var values))
                series.Add((inspector, area), new(sample));
            else
            {
                values.Count++;
                if (values.Warm.Count == WarmSampleLimit) values.Warm.Dequeue();
                values.Warm.Enqueue(sample);
            }
        }
    }

    public readonly struct Scope : IDisposable
    {
        private readonly PerformanceRecorder? owner;
        private readonly Inspector? inspector;
        private readonly PerformanceArea area;
        private readonly long epoch, started, allocated;
        private readonly int thread;

        internal Scope(PerformanceRecorder owner, Inspector? inspector, PerformanceArea area, long epoch)
        {
            this.owner = owner; this.inspector = inspector; this.area = area; this.epoch = epoch;
            thread = Environment.CurrentManagedThreadId;
            allocated = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            // Only synchronous scopes: never mix allocation counters across threads/await boundaries.
            if (owner == null || thread != Environment.CurrentManagedThreadId) return;
            var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            owner.Record(inspector, area, epoch, milliseconds, bytes);
        }
    }
}
