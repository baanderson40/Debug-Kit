using System.Collections.Immutable;
using DebugKit.Core;

internal static class ObservationTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("manual observations retain bounded samples only after Stop", Lifecycle),
        ("observation rollover counts dropped oldest samples", Rollover),
        ("observation context cancellation discards active samples", Cancellation),
    ];

    private static void Lifecycle()
    {
        var source = new Source(); using var engine = new DiagnosticEngine(source);
        Equal(QueueStatus.Accepted, engine.EnqueueTool(Inspector.TargetDistanceTracker, new()));
        engine.Tick(); engine.Tick(); engine.Tick();
        Check(engine.View() is { ActiveRequest: not null, Results.Length: 0 });
        Check(engine.StopObservation()); engine.Tick();
        var result = engine.View().Results.Single();
        Equal(Outcome.Success, result.Outcome); Check(result.NonAtomic);
        Equal(3, ((ObservationPayload)result.Payload!).Samples.Length);
        var json = ReportCodec.Create(engine.View(), Provenance.Create("1.0.0", "15.0.0"), ReportFormat.Json, DateTimeOffset.UnixEpoch).Content;
        Check(json.Contains("observation", StringComparison.Ordinal));
        var text = ReportCodec.Create(engine.View(), Provenance.Create("1.0.0", "15.0.0"), ReportFormat.Text, DateTimeOffset.UnixEpoch).Content;
        Check(text.Contains("older samples dropped", StringComparison.Ordinal) && text.Contains("sample", StringComparison.Ordinal));
        Check(source.Ended);
    }

    private static void Rollover()
    {
        var payload = new ObservationPayload([], 0, "test");
        for (var i = 0; i < 130; i++)
            payload = ObservationPolicy.Append(payload, new([new(DateTimeOffset.UnixEpoch.AddSeconds(i), [])], 0, "test"));
        Equal(128, payload.Samples.Length); Equal(2L, payload.Dropped);
        Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), payload.Samples[0].ObservedUtc);
    }

    private static void Cancellation()
    {
        var source = new Source(); using var engine = new DiagnosticEngine(source);
        engine.EnqueueTool(Inspector.TargetDistanceTracker, new()); engine.Tick(); engine.Tick();
        engine.Cancel();
        Check(engine.View().Results.IsEmpty); Check(source.Ended);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Observation assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }

    private sealed class Source : IGameSource, IToolSource, IObservationToolSource
    {
        private int samples;
        public bool Ended { get; private set; }
        public LiveContext ReadContext() => new(new(true, 1, true, new(0, 0, 0), 1, 1, new(false, false, false, false, false)), 1);
        public int ObjectTableLength => 0;
        public ObjectSample? ReadObject(int index) => null;
        public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? expectedStamp) => new(null);
        public ToolStep SampleObservation(Inspector inspector, ToolOptions options)
        {
            samples++;
            return new(new ObservationPayload([new(DateTimeOffset.UnixEpoch.AddSeconds(samples), [new("test", "sample", "int", samples.ToString(), FieldAvailability.Available)])], 0, inspector.ToString()),
                Complete: false, Examined: 1, Matched: 1);
        }
        public void EndObservation(Inspector inspector) => Ended = true;
    }
}
