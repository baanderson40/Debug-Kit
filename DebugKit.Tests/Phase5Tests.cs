using System.Collections.Immutable;
using DebugKit.Core;

internal static class Phase5Tests
{
    public static (string Name, Action Run)[] All =>
    [
        ("idle profiling view and preview never start provider or network captures", NoAutomaticRequests),
        ("network consent and malformed scopes reject before async source start", InvalidRequests),
        ("cancel stops active and queued network requests without starting queued work", CancelQueued),
        ("clear history does not cancel active network capture", ClearActive),
        ("late cancelled response cannot overwrite a newer capture or its inputs", LateReplacement),
        ("disposed engine late response cannot contaminate replacement engine", ReloadIsolation),
        ("faulted async source is sanitized and does not poison later captures", FaultRecovery),
        ("logout loading territory and character changes cancel active and queued requests", TransitionMatrix),
        ("explicit lifecycle invalidation cancels even before any network task starts", EventBeforeStart),
        ("network payload and history byte bounds hold through repeated captures", NetworkHistoryBounds),
        ("provider unload after success never reuses previous boolean response", ProviderUnload),
    ];
    private static void Check(bool condition) { if (!condition) throw new Exception("Phase 5 assertion failed"); }
    private static ToolOptions Options(uint item = 5333) => new(ItemId: item, WorldId: 74, NetworkConsent: true);
    private static ToolStep Response(int status = 200) => new(new NetworkPayload(NetworkService.Universalis,
        new FakeClock().UtcNow, [new EvidenceRow("summary", "Synthetic test evidence", [new("Price", "1")])]),
        Examined: 1, Matched: 1, HttpStatusCode: status);

    private sealed class Source : IGameSource, IToolSource, IAsyncToolSource
    {
        public LiveContext Context = new(new(true, 100, true, new(0, 0, 0), 19, 100, new(false, false, false, false, false)), 1);
        public readonly List<(TaskCompletionSource<ToolStep> Completion, CancellationToken Token, ToolOptions Options)> Requests = [];
        public Func<ToolStep>? Read;
        public int SynchronousCalls;
        public LiveContext ReadContext() => Context;
        public int ObjectTableLength => 0;
        public ObjectSample? ReadObject(int index) => null;
        public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? stamp)
        { SynchronousCalls++; return Read?.Invoke() ?? throw new InvalidOperationException(); }
        public Task<ToolStep> StartNetworkCapture(Inspector inspector, ToolOptions options, CancellationToken token)
        {
            var completion = new TaskCompletionSource<ToolStep>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((completion, token, options)); return completion.Task;
        }
    }
    private static void NoAutomaticRequests()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.Performance.Enabled = true;
        for (var i = 0; i < 200; i++) engine.Tick();
        foreach (var format in new[] { ReportFormat.Json, ReportFormat.Text })
            _ = ReportCodec.Create(engine.View(), Provenance.Create("0.4.2", "15.0.3.9"), format, DateTimeOffset.UtcNow);
        Check(source.Requests.Count == 0 && source.SynchronousCalls == 0 && engine.View().Results.IsEmpty);
    }
    private static void InvalidRequests()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        foreach (var options in new[] { Options() with { NetworkConsent = false }, Options() with { ItemId = 0 },
            Options() with { WorldId = 0 }, Options() with { MarketScope = (MarketScope)99 } })
            Check(engine.EnqueueTool(Inspector.MarketSummary, options) == QueueStatus.Invalid);
        engine.Tick(); Check(source.Requests.Count == 0 && engine.View().Results.IsEmpty);
    }
    private static void StartPair(DiagnosticEngine engine)
    {
        Check(engine.EnqueueTool(Inspector.MarketSummary, Options()) == QueueStatus.Accepted);
        Check(engine.EnqueueTool(Inspector.MarketSummary, Options(5334)) == QueueStatus.Accepted);
        engine.Tick();
    }
    private static void CancelQueued()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        StartPair(engine); engine.Cancel();
        Check(source.Requests.Count == 1 && source.Requests[0].Token.IsCancellationRequested);
        Check(engine.View().Results.Length == 2 && engine.View().Results.All(r => r.Reason == Reason.UserCancelled));
        source.Requests[0].Completion.SetResult(Response()); engine.Tick();
        Check(source.Requests.Count == 1 && engine.View().Pending == 0 && engine.View().Results.All(r => r.Payload == null && r.HttpStatusCode == null));
    }
    private static void ClearActive()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.Enqueue(Inspector.Context); engine.Tick();
        engine.EnqueueTool(Inspector.MarketSummary, Options()); engine.Tick();
        var id = engine.View().ActiveRequest;
        engine.ClearResults();
        Check(engine.View().Results.IsEmpty && engine.View().ActiveRequest == id && !source.Requests[0].Token.IsCancellationRequested);
        source.Requests[0].Completion.SetResult(Response()); engine.Tick();
        Check(engine.View().Results.Length == 1 && engine.View().Results[0].RequestId == id && engine.View().Results[0].Outcome == Outcome.Success);
    }
    private static void LateReplacement()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.MarketSummary, Options()); engine.Tick(); engine.Cancel();
        engine.EnqueueTool(Inspector.MarketSummary, Options(5334)); engine.Tick();
        var replacement = engine.View().ActiveRequest;
        source.Requests[0].Completion.SetResult(Response(201)); engine.Tick();
        Check(engine.View().ActiveRequest == replacement && engine.View().Results.Length == 1);
        source.Requests[1].Completion.SetResult(Response()); engine.Tick();
        var latest = engine.View().Results[^1];
        Check(latest.RequestId == replacement && latest.ToolOptions!.ItemId == 5334 && latest.HttpStatusCode == 200);
        Check(engine.View().Results[0].Payload == null && engine.View().Results[0].HttpStatusCode == null);
    }
    private static void ReloadIsolation()
    {
        var source = new Source();
        using var old = new DiagnosticEngine(source, new FakeClock());
        old.EnqueueTool(Inspector.MarketSummary, Options()); old.Tick(); old.Dispose();
        using var next = new DiagnosticEngine(source, new FakeClock());
        next.EnqueueTool(Inspector.MarketSummary, Options(5334)); next.Tick();
        source.Requests[0].Completion.SetResult(Response(201)); old.Tick(); next.Tick();
        Check(source.Requests[0].Token.IsCancellationRequested && next.View().Results.IsEmpty);
        source.Requests[1].Completion.SetResult(Response()); next.Tick();
        Check(next.View().Results.Length == 1 && next.View().Results[0].ToolOptions!.ItemId == 5334);
        Check(old.EnqueueTool(Inspector.MarketSummary, Options()) == QueueStatus.Disposed);
    }
    private static void FaultRecovery()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.MarketSummary, Options()); engine.Tick();
        source.Requests[0].Completion.SetException(new InvalidOperationException("PRIVATE TOKEN /secret/path")); engine.Tick();
        Check(engine.View().Results[^1].Outcome == Outcome.Failed && engine.View().Results[^1].Payload == null);
        var report = ReportCodec.Create(engine.View(), Provenance.Create("0.4.2", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
        Check(!report.Content.Contains("PRIVATE") && !report.Content.Contains("/secret/path"));
        engine.EnqueueTool(Inspector.MarketSummary, Options()); engine.Tick();
        source.Requests[1].Completion.SetResult(Response()); engine.Tick();
        Check(engine.View().Results[^1].Outcome == Outcome.Success);
    }
    private static void TransitionMatrix()
    {
        foreach (var change in new Func<LiveContext, LiveContext>[]
        {
            c => c with { Data = c.Data with { LoggedIn = false } },
            c => c with { Data = c.Data with { Conditions = c.Data.Conditions with { BetweenAreas = true } } },
            c => c with { Data = c.Data with { TerritoryId = 101 } },
            c => c with { CharacterToken = 2 },
        })
        {
            var source = new Source();
            using var engine = new DiagnosticEngine(source, new FakeClock());
            StartPair(engine); source.Context = change(source.Context); engine.Tick();
            Check(source.Requests.Count == 1 && source.Requests[0].Token.IsCancellationRequested);
            Check(engine.View().Results.Length == 2 && engine.View().Results.All(r => r.Reason == Reason.ContextChanged));
            source.Requests[0].Completion.SetResult(Response()); engine.Tick();
            Check(source.Requests.Count == 1 && engine.View().Results.All(r => r.Payload == null));
        }
    }
    private static void EventBeforeStart()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.MarketSummary, Options()); engine.Invalidate(); engine.Tick();
        Check(source.Requests.Count == 0 && engine.View().Results[^1].Reason == Reason.ContextChanged);
    }
    private static void NetworkHistoryBounds()
    {
        var source = new Source();
        var fields = Enumerable.Range(0, 20).Select(i => new FieldRow($"Field{i}", new string('x', 512))).ToImmutableArray();
        var rows = Enumerable.Range(0, 50).Select(i => new EvidenceRow($"row/{i}", "Synthetic", fields)).ToImmutableArray();
        using var engine = new DiagnosticEngine(source, new FakeClock(), new Limits(Results: 3, ResultBytes: 2048, SessionBytes: 8192));
        for (var i = 0; i < 8; i++)
        {
            engine.EnqueueTool(Inspector.MarketSummary, Options((uint)(5333 + i))); engine.Tick();
            source.Requests[^1].Completion.SetResult(new(new NetworkPayload(NetworkService.Universalis, new FakeClock().UtcNow, rows), HttpStatusCode: 200));
            engine.Tick();
            var latest = engine.View().Results[^1];
            Check(latest.Partial && latest.Omitted > 0 && latest.HttpStatusCode == 200 && ReportCodec.ResultSize(latest) <= 2048);
        }
        Check(engine.View().Results.Length <= 3 && engine.View().Evicted >= 5);
        Check(engine.View().Results.Sum(ReportCodec.ResultSize) <= 8192);
    }
    private static void ProviderUnload()
    {
        var loaded = true; var calls = 0;
        ProviderPayload Metadata() => ProviderCatalog.Metadata([new("AutoRetainer", "4.6.2.18", loaded, false)], true);
        var source = new Source { Read = () => ProviderQueries.Capture(ProviderKind.AutoRetainer, Metadata, _ => { calls++; return true; }) };
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.ProviderReadiness, new()); engine.Tick();
        Check(engine.View().Results[^1].Payload is ProviderQueryPayload { Value: true });
        loaded = false; engine.EnqueueTool(Inspector.ProviderReadiness, new()); engine.Tick();
        Check(calls == 1 && engine.View().Results.Length == 2 && engine.View().Results[^1].Reason == Reason.ProviderUnavailable);
        Check(engine.View().Results[^1].Payload is ProviderPayload p && p.Rows[0].Loaded == ObservationState.No);
    }
}
