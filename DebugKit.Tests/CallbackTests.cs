using System.Collections.Immutable;
using System.Text.Json;
using DebugKit.Core;

internal static class CallbackTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("callback observation allows all addons or one bounded exact filter", Inputs),
        ("callback options freeze authorization and discard unrelated inputs", Canonical),
        ("callback buffer rolls over and retains newest typed values with a dropped count", BufferBounds),
        ("callback payload reports argument omissions and trims through shared bounds", PayloadBounds),
        ("callback engine awaits once without per-frame no-progress failure", Awaiting),
        ("callback cancellation discards late event payloads and signals local stop", Cancel),
        ("callback observation runs until explicitly stopped", Timeout),
        ("callback context replacement cancels observation without retaining new-context events", ContextChange),
        ("callback generated reports preserve boolean evidence without addresses or replay", Reports),
        ("callback integer values are retained by default with strict type and width checks", NumericRetention),
        ("callback formatting matches typed signed unsigned and boolean table values", TypedValues),
        ("callback integer type widths and excluded union fields fail closed", NumericTypes),
        ("callback options discard unrelated inputs", NumericCanonical),
        ("cancelled callback observation cannot leak rows into a later capture", NumericCancellation),
        ("callback values and privacy warning survive reports and exports", NumericReports),
        ("blank callback filter matches all addons while explicit filter stays exact", AllAddonFilter),
        ("callback addon names use only the bounded native name buffer", BoundedAddonNames),
        ("all-addon callback reports preserve each event addon and blank requested scope", AllAddonReports),
        ("all-addon rolling event cap is global across different addon names", AllAddonLimit),
    ];
    private static void Check(bool condition) { if (!condition) throw new Exception("Callback assertion failed"); }
    private static ToolOptions Options => new(Addon: "Character", ExperimentalNative: true);
    private static CallbackEntry Entry(uint count = 2) => new(new FakeClock().UtcNow, count, false, true, [new(0, 2, false), new(1, 3, true)]);
    private static CallbackPayload Payload() => new("Character", NativeCompatibility.GameBuild, new FakeClock().UtcNow,
        new FakeClock().UtcNow, [Entry()], 0, 0);
    private static void Inputs()
    {
        Check(Options.IsValid(Inspector.AddonCallbacks));
        Check((Options with { Addon = "" }).IsValid(Inspector.AddonCallbacks));
        foreach (var options in new[] { Options with { Addon = "Character*" }, Options with { Addon = "../Character" }, Options with { Addon = new string('a', 32) } })
            Check(!options.IsValid(Inspector.AddonCallbacks));
        Check(Catalog.Get(Inspector.AddonCallbacks).RequiresPlayer && !Catalog.Get(Inspector.AddonCallbacks).BulkSafe);
    }
    private static void Canonical()
    {
        Check((Options with { ItemId = 5, NetworkConsent = true, NameFilter = "ignored" }).Canonical(Inspector.AddonCallbacks) == Options);
        Check((Options with { ExperimentalNative = false }).Canonical(Inspector.AddonCallbacks).ExperimentalNative == false);
    }
    private static void BufferBounds()
    {
        var buffer = new CallbackBuffer();
        for (var i = 0; i < 1000; i++) buffer.Add(Entry() with { Addon = $"A{i}" });
        var rows = buffer.Snapshot();
        Check(buffer.HasDroppedEvents && buffer.Dropped == 936 && rows.Length == 64 && rows[0].Addon == "A936" && rows[^1].Addon == "A999" &&
            rows[0].Arguments[0].Boolean == false && rows[0].Arguments[1].Boolean == null);
        buffer.Add(Entry()); Check(buffer.Snapshot().Length == 64 && buffer.Dropped == 937);
        var longArguments = Entry(20) with { Arguments = Enumerable.Range(0, 20).Select(i => new CallbackArgument(i, 2, true)).ToImmutableArray() };
        var clean = CallbackBuffer.Sanitize(longArguments);
        Check(clean.Arguments.Length == 16 && clean.ArgumentsOmitted == 4);
        Check(CallbackBuffer.Sanitize(Entry(0)).Arguments.IsEmpty);
    }
    private static void PayloadBounds()
    {
        var p = Payload() with { Rows = Enumerable.Repeat(Entry(1000), 100).ToImmutableArray() };
        var clean = (CallbackPayload)Payloads.Sanitize(p)!;
        Check(clean.Rows.Length == 64 && clean.Rows[0].ArgumentsOmitted == 998 && clean.Rows[0].Arguments[1].Boolean == null);
        Check(Payloads.Count(Payloads.TrimLast(clean)) == 63);
    }
    private sealed class Source : IGameSource, IToolSource, ICallbackToolSource
    {
        public LiveContext Context = new FakeSource(0).Context;
        public TaskCompletionSource<ToolStep> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts;
        public int StopRequests;
        public CancellationToken Token;
        public LiveContext ReadContext() => Context;
        public int ObjectTableLength => 0;
        public ObjectSample? ReadObject(int index) => null;
        public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? stamp) => throw new Exception("Must await observation");
        public Task<ToolStep> StartCallbackCapture(ToolOptions options, CancellationToken token)
        { Starts++; Token = token; return Completion.Task; }
        public void RequestCallbackStop() => StopRequests++;
    }
    private static void Awaiting()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options);
        for (var i = 0; i < 300; i++) engine.Tick();
        Check(source.Starts == 1 && engine.View().Results.IsEmpty);
        source.Completion.SetResult(new(Payload())); engine.Tick();
        Check(engine.View().Results[^1] is { Outcome: Outcome.Success, Payload: CallbackPayload, Steps: 1, NonAtomic: true });
    }
    private static void Cancel()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick(); engine.Cancel();
        Check(source.Token.IsCancellationRequested);
        source.Completion.SetResult(new(Payload())); engine.Tick();
        Check(engine.View().Results.Length == 1 && engine.View().Results[0] is { Reason: Reason.UserCancelled, Payload: null });
    }
    private static void Timeout()
    {
        var source = new Source(); var clock = new FakeClock();
        using var engine = new DiagnosticEngine(source, clock);
        engine.EnqueueTool(Inspector.AddonCallbacks, Options);
        Check(!engine.CanStartCallbackCapture);
        engine.Tick(); Check(engine.HasCallbackCapture);
        clock.Milliseconds += 60000; engine.Tick();
        Check(!source.Token.IsCancellationRequested && engine.View().Results.IsEmpty && engine.HasCallbackCapture);
        Check(engine.StopCallbackCapture() && source.StopRequests == 1);
        source.Completion.SetResult(new(Payload())); engine.Tick();
        Check(engine.View().Results[^1] is { Outcome: Outcome.Success, Payload: CallbackPayload { Rows.Length: 1 } });
    }
    private static void ContextChange()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick();
        source.Context = source.Context with { CharacterToken = 999 }; engine.Tick();
        Check(source.Token.IsCancellationRequested && engine.View().Results[^1].Reason == Reason.ContextChanged);
        source.Completion.SetResult(new(Payload())); engine.Tick(); Check(engine.View().Results[^1].Payload == null);
    }
    private static void Reports()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick(); source.Completion.SetResult(new(Payload())); engine.Tick();
        var result = engine.View().Results[^1];
        Check(ReportCodec.Describe(result, 0).Contains("exact addon Character") && ReportCodec.Describe(result, 0).Contains("1 [Int]: omitted"));
        Check(SnapshotComparer.Compare(result, result with { RequestId = Guid.NewGuid() }, false).Reason == Reason.IncompatibleSnapshots);
        var preview = ReportCodec.Create(engine.View(), Provenance.Create("0.5.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(preview.Content);
        var payload = json.RootElement.GetProperty("Results")[0].GetProperty("Payload");
        Check(payload.GetProperty("$type").GetString() == "callbacks" && payload.GetProperty("Rows")[0].GetProperty("Arguments")[1].GetProperty("Boolean").ValueKind == JsonValueKind.Null);
    }
    private static CallbackPayload NumericPayload() => Payload() with { Rows =
    [new(new FakeClock().UtcNow, 4, true, false,
        [new(0, 3, null, Signed: 23), new(1, 3, null, Signed: -1), new(2, 5, null, Unsigned: 0), new(3, 6, null, Unsigned: ulong.MaxValue)])] };
    private static CaptureResult Capture(CallbackPayload payload)
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick();
        source.Completion.SetResult(new(payload)); engine.Tick();
        return engine.View().Results[^1];
    }
    private static void NumericRetention()
    {
        var entry = NumericPayload().Rows[0];
        var safe = new CallbackBuffer(); safe.Add(entry);
        Check(safe.Snapshot()[0].Arguments[0].Signed == 23 && safe.Snapshot()[0].Arguments[1].Signed == -1 &&
            safe.Snapshot()[0].Arguments[2].Unsigned == 0 && safe.Snapshot()[0].Arguments[3].Unsigned == ulong.MaxValue);
        var admitted = (CallbackPayload)Capture(NumericPayload()).Payload!;
        Check(admitted.Rows[0].Arguments[1].Signed == -1 && admitted.Rows[0].Arguments[3].Unsigned == ulong.MaxValue);
    }
    private static void TypedValues()
    {
        Check(CallbackText.Argument(new(0, 3, null, Signed: 23)) == "0 [Int]: 23");
        Check(CallbackText.Argument(new(1, 3, null, Signed: -1)) == "1 [Int]: -1");
        Check(CallbackText.Argument(new(1, 5, null, Unsigned: 0)) == "1 [UInt]: 0");
        Check(CallbackText.Argument(new(0, 2, false)) == "0 [Bool]: 0");
        Check(CallbackText.Argument(new(0, 8, null)) == "0 [String]: omitted");
        Check(CallbackText.Boolean(true) == "1" && CallbackText.Boolean(false) == "0");
        var values = new CallbackEntry(new FakeClock().UtcNow, 4, true, false,
            [new(0, 3, null, Signed: int.MinValue), new(1, 5, null, Unsigned: uint.MaxValue),
             new(2, 4, null, Signed: long.MinValue), new(3, 6, null, Unsigned: ulong.MaxValue)]);
        var clean = CallbackBuffer.Sanitize(values);
        Check(clean.Arguments[0].Signed == int.MinValue && clean.Arguments[1].Unsigned == uint.MaxValue &&
            clean.Arguments[2].Signed == long.MinValue && clean.Arguments[3].Unsigned == ulong.MaxValue);
        Check(CallbackText.Argument(clean.Arguments[3]) == "3 [UInt64]: 18446744073709551615");
    }
    private static void NumericTypes()
    {
        var values = new CallbackEntry(new FakeClock().UtcNow, 6, false, false,
            [new(0, 3, true, Signed: long.MaxValue, Unsigned: 1), new(1, 5, true, Signed: -1, Unsigned: ulong.MaxValue),
             new(2, 8, true, Signed: 1, Unsigned: 1), new(3, 12, true, Signed: 1, Unsigned: 1),
             new(4, 7, true, Signed: 1, Unsigned: 1), new(5, 2, false, Signed: 1, Unsigned: 1)]);
        var clean = CallbackBuffer.Sanitize(values);
        Check(clean.Arguments.All(a => a.Signed == null && a.Unsigned == null));
        Check(clean.Arguments.Take(5).All(a => a.Boolean == null) && clean.Arguments[5].Boolean == false);
    }
    private static void NumericCanonical()
    {
        Check(Options.Canonical(Inspector.AddonCallbacks) == Options);
        Check(Options.Canonical(Inspector.Target) == new ToolOptions());
        Check(Catalog.Get(Inspector.AddonCallbacks).Version == 4);
    }
    private static void NumericCancellation()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick();
        var old = source.Completion;
        engine.Cancel(); source.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EnqueueTool(Inspector.AddonCallbacks, Options); engine.Tick();
        old.SetResult(new(NumericPayload())); engine.Tick();
        Check(engine.View().Results.Length == 1 && engine.View().Results[0].Payload == null);
        source.Completion.SetResult(new(NumericPayload())); engine.Tick();
        Check(engine.View().Results[^1].Payload is CallbackPayload p && p.Rows[0].Arguments[1].Signed == -1);
    }
    private static void NumericReports()
    {
        var result = Capture(NumericPayload());
        var view = new EngineView(0, 0, null, 0, [result], 0, 0, 0, true);
        var text = ReportCodec.Describe(result, 0);
        Check(text.Contains("0 [Int]: 23") && text.Contains("1 [Int]: -1") && text.Contains("2 [UInt]: 0") && text.Contains("raw flag 1 | return 0"));
        Check(text.Contains(CallbackText.NumericWarning));
        var directory = Path.Combine(Path.GetTempPath(), "debugkit-callback-integers-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var exporter = new ReportExporter(directory);
            foreach (var format in new[] { ReportFormat.Json, ReportFormat.Text })
            {
                var preview = ReportCodec.Create(view, Provenance.Create("0.5.1", "15.0.3.9"), format, DateTimeOffset.UtcNow);
                if (format == ReportFormat.Json)
                {
                    using var json = JsonDocument.Parse(preview.Content);
                    var record = json.RootElement.GetProperty("Results")[0];
                    var arguments = record.GetProperty("Payload").GetProperty("Rows")[0].GetProperty("Arguments");
                    Check(arguments[1].GetProperty("Signed").GetInt64() == -1 && arguments[3].GetProperty("Unsigned").GetUInt64() == ulong.MaxValue);
                    var privacy = json.RootElement.GetProperty("Manifest").GetProperty("PrivacyPolicy").GetString()!;
                    Check(privacy.Contains(CallbackText.NumericWarning) && !privacy.Contains("No character/object/owner IDs"));
                }
                else Check(preview.Content.Contains(CallbackText.NumericWarning) && preview.Content.Contains("1 [Int]: -1"));
                Check(exporter.ExportAsync(preview).GetAwaiter().GetResult() == ExportStatus.Success);
                var path = Path.Combine(directory, preview.FileName);
                Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(preview.Bytes.AsSpan())); File.Delete(path);
            }
        }
        finally { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
    }
    private static void AllAddonFilter()
    {
        Check(CallbackNames.Matches("", "Character") && CallbackNames.Matches("", "Inventory") && CallbackNames.Matches("", null));
        Check(CallbackNames.Matches("Character", "Character") && !CallbackNames.Matches("Character", "CharacterPreview"));
        Check(!CallbackNames.Matches("Character", "character") && !CallbackNames.Matches("Character", null));
        var options = Options with { Addon = "" };
        Check(options.IsValid(Inspector.AddonCallbacks) && options.Canonical(Inspector.AddonCallbacks) == options);
    }
    private static void BoundedAddonNames()
    {
        var buffer = new byte[64];
        System.Text.Encoding.ASCII.GetBytes("_PartyList").CopyTo(buffer, 0);
        Check(CallbackNames.Read(buffer) == "_PartyList");
        buffer.AsSpan(32).Fill((byte)'x'); Check(CallbackNames.Read(buffer) == "_PartyList");
        Check(CallbackNames.Read(buffer.AsSpan(0, 16)) == null);
        buffer.AsSpan(0, 32).Fill((byte)'a'); Check(CallbackNames.Read(buffer) == null);
        buffer[31] = 0; Check(CallbackNames.Read(buffer) == new string('a', 31));
        buffer[0] = 0xFF; Check(CallbackNames.Read(buffer) == null);
        Check(CallbackNames.Clean("../private") == null && CallbackNames.Clean("Character") == "Character");
    }
    private static void AllAddonReports()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.AddonCallbacks, Options with { Addon = "" }); engine.Tick();
        var payload = NumericPayload() with { Addon = "", Rows =
            [NumericPayload().Rows[0] with { Addon = "Character" }, NumericPayload().Rows[0] with { Addon = "Inventory" }] };
        source.Completion.SetResult(new(payload)); engine.Tick();
        var result = engine.View().Results[^1];
        var retained = (CallbackPayload)result.Payload!;
        Check(retained.Addon == "" && retained.Rows[0].Addon == "Character" && retained.Rows[1].Addon == "Inventory");
        var text = ReportCodec.Describe(result, 0);
        Check(text.Contains("Captured inputs: all addons") && text.Contains("scope all addons") && text.Contains("Character |") && text.Contains("Inventory |"));
        Check(CallbackNames.Display(retained, retained.Rows[0]) == "Character" && CallbackNames.Display(retained, Entry()) == "unknown");
        Check(CallbackNames.Display(Payload(), Entry()) == "Character");
        var preview = ReportCodec.Create(engine.View(), Provenance.Create("0.5.2", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(preview.Content);
        var record = json.RootElement.GetProperty("Results")[0];
        Check(record.GetProperty("ToolOptions").GetProperty("Addon").GetString() == "");
        Check(record.GetProperty("Payload").GetProperty("Rows")[1].GetProperty("Addon").GetString() == "Inventory");
    }
    private static void AllAddonLimit()
    {
        var buffer = new CallbackBuffer();
        for (var i = 0; i < 100; i++) buffer.Add(NumericPayload().Rows[0] with { Addon = i % 2 == 0 ? "Character" : "Inventory" });
        var rows = buffer.Snapshot();
        Check(buffer.HasDroppedEvents && buffer.Dropped == 36 && rows.Length == 64 && rows.Count(r => r.Addon == "Character") == 32 && rows.Count(r => r.Addon == "Inventory") == 32);
    }
}
