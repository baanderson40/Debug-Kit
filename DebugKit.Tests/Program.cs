using System.Collections.Immutable;
using System.Text.Json;
using DebugKit.Core;

var tests = new (string Name, Action Run)[]
{
    ("catalog is standalone and bounded", CatalogTest),
    ("new useful-script ports are explicit and bounded", UsefulPortModels),
    ("radius and base-ID validation rejects invalid inputs", Validation),
    ("queue deduplicates and rejects overflow before source reads", QueueBounds),
    ("logged-out context preserves unknowns", LoggedOutContext),
    ("loading context never exposes player fields", LoadingContext),
    ("object scan blocked when logged out", LoggedOutObjects),
    ("object scan blocked during transition", LoadingObjects),
    ("missing player is unavailable not empty success", MissingPlayer),
    ("missing object table is unavailable not empty success", MissingTable),
    ("context result includes safe flags and provenance", ContextResult),
    ("nonfinite player position stays unavailable", InvalidOrigin),
    ("objects are filtered by radius, kind, base ID", Filtering),
    ("scan distinguishes empty invalid filtered and unreadable entries", ScanCounters),
    ("counter accounting survives partial capture and cancellation", PartialCounters),
    ("Other retains safe native-kind label and value", NativeKindDetails),
    ("future native kinds retain numeric value without arbitrary labels", UnknownNativeKind),
    ("object report v3 exports names kind details and separate counters", ObjectReportSchema),
    ("display names retained for NPC player companion and retainer kinds", DisplayNames),
    ("display names bounded single-line and safe for report layout", DisplayNameBounds),
    ("missing display names preserve useful object rows", MissingNames),
    ("named captures still obey result byte limits", NamedPayloadLimit),
    ("scan uses bounded index continuation and labels non-atomic", Incremental),
    ("cooperative elapsed budget yields between reads", TimeBudget),
    ("row cap marks partial without extra reads", RowCap),
    ("examined-entry cap marks partial", EntryCap),
    ("finite-coordinate validation skips invalid objects", FiniteCoordinates),
    ("disappearing objects are skipped without wrapper retention", Disappearing),
    ("unreadable objects remain unavailable", UnreadableObjects),
    ("character replacement invalidates capture in same territory", CharacterChange),
    ("territory changes invalidate pending work and history freshness", TerritoryChange),
    ("availability and transition changes invalidate capture", AvailabilityChange),
    ("mid-step context replacement discards current-context claim", MidStepChange),
    ("event invalidation cancels queued requests", ExplicitInvalidation),
    ("cancel terminates active and pending work", Cancellation),
    ("clear affects history not queued work", ClearHistory),
    ("latest failed attempt preserves last success", LastSuccess),
    ("history entry cap evicts oldest", HistoryCap),
    ("result byte cap trims and reports omitted rows", ResultByteCap),
    ("session byte cap evicts within limit", SessionByteCap),
    ("hard limits reject unsafe expansion", InvalidLimits),
    ("private identity and error strings never enter reports", Privacy),
    ("unknown and player base IDs omitted", IdentityFields),
    ("version provenance rejects arbitrary strings", VersionPrivacy),
    ("manifest revalidates caller-supplied provenance", ManifestPrivacy),
    ("JSON and text carry stable report schema", ReportFormats),
    ("UTC clock reversal does not make duration negative", ClockJump),
    ("dispose prevents late captures and clears transient identity", Disposal),
    ("export uses preview bytes and never overwrites", ExportExact),
    ("cancelled export leaves no report or temporary file", ExportCancellation),
    ("export failure is sanitized and narrowly cleaned", ExportFailure),
    ("disposed exporter rejects late work", ExportDisposal),
    ("oversized export rejected without artifacts", OversizedExport),
}.Concat(Phase2Tests.All).Concat(Phase3Tests.All).Concat(Phase4Tests.All).Concat(Phase5Tests.All).Concat(CallbackTests.All).Concat(ObservationTests.All).ToArray();
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed. Offline Core/menu-decoder tests only; no live game/UI coverage.");
return failed == 0 ? 0 : 1;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; actual {actual}"); }
static FakeSource Source(int length = 4) => new(length);
static CaptureResult Last(DiagnosticEngine engine) => engine.View().Results[^1];
static void Drain(DiagnosticEngine engine)
{
    for (var i = 0; i < 2000; i++)
    {
        engine.Tick();
        var v = engine.View();
        if (v.Pending == 0 && v.ActiveRequest == null) return;
    }
    throw new Exception("Coordinator did not drain");
}
static DiagnosticEngine Capture(FakeSource source, Inspector inspector = Inspector.NearbyObjects, CaptureOptions? options = null, Limits? limits = null)
{
    var engine = new DiagnosticEngine(source, new FakeClock(), limits);
    Equal(QueueStatus.Accepted, engine.Enqueue(inspector, options));
    Drain(engine);
    return engine;
}
static ObjectSample Npc(float x = 1, uint id = 42) => new(ObjectCategory.BattleNpc, id, new(x, 0, 0), true, true);

static void CatalogTest()
{
    Equal(39, Catalog.All.Length);
    Equal(2, Catalog.All.Count(x => x.BulkSafe));
    Equal(1, Catalog.Get(Inspector.Context).Version);
    Equal(3, Catalog.Get(Inspector.NearbyObjects).Version);
    Equal("game.context", Catalog.Get(Inspector.Context).Id);
    Equal("objects.nearby", Catalog.Get(Inspector.NearbyObjects).Id);
}
static void UsefulPortModels()
{
    foreach (var inspector in new[] { Inspector.BigDump, Inspector.ConditionFlags, Inspector.PlayerStatuses,
        Inspector.InstalledPlugins, Inspector.JobLookup, Inspector.NpcNameLookup, Inspector.TerritoryLookup,
        Inspector.MovementSpeedTracker, Inspector.TargetDistanceTracker, Inspector.EntityNameWatch })
        Check(Catalog.All.Any(x => x.Inspector == inspector), $"Missing catalog entry {inspector}");
    Check(new ToolOptions(RowId: 1).IsValid(Inspector.JobLookup));
    Check(new ToolOptions(RowId: 1, Sheet: SheetKind.BNpcName).IsValid(Inspector.NpcNameLookup));
    Check(!new ToolOptions(RowId: 0).IsValid(Inspector.JobLookup));
    var fields = (DiagnosticFieldsPayload)Payloads.Sanitize(new DiagnosticFieldsPayload([
        new("Player", "Position", "Vector3", "<1, 2, 3>", FieldAvailability.Available),
        new("Targets", "Focus", "slot", null, FieldAvailability.Unsupported),
        new("Privacy", "Object ID", "identifier", "123456", FieldAvailability.Withheld),
    ], "unit test"))!;
    Equal(3, fields.Fields.Length);
    Equal(FieldAvailability.Unsupported, fields.Fields[1].Availability);
    Equal(FieldAvailability.Withheld, fields.Fields[2].Availability);
    var plugins = (PluginInventoryPayload)Payloads.Sanitize(new PluginInventoryPayload([
        new("ExamplePlugin", true, "1.2.3"), new("OtherPlugin", false, "unsafe/path")], true))!;
    Equal("Unavailable", plugins.Rows[1].Version);
    Equal(2, Payloads.Count(plugins));
}
static void Validation()
{
    using var e = new DiagnosticEngine(Source());
    foreach (var radius in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, -1, 201 })
        Equal(QueueStatus.Invalid, e.Enqueue(Inspector.NearbyObjects, new(radius)));
    Equal(QueueStatus.Invalid, e.Enqueue(Inspector.NearbyObjects, new(BaseId: 0)));
    Equal(QueueStatus.Invalid, e.Enqueue((Inspector)900));
    Equal(QueueStatus.Invalid, e.Enqueue(Inspector.NearbyObjects, new(Kind: (ObjectCategory)900)));
    Check(new CaptureOptions(1).IsValid && new CaptureOptions(200, BaseId: uint.MaxValue).IsValid);
    Equal(0, e.View().Pending);
}
static void QueueBounds()
{
    var s = Source(); using var e = new DiagnosticEngine(s, new FakeClock(), new(Queue: 2));
    Equal(QueueStatus.Accepted, e.Enqueue(Inspector.Context));
    Equal(QueueStatus.Duplicate, e.Enqueue(Inspector.Context));
    Equal(QueueStatus.Accepted, e.Enqueue(Inspector.NearbyObjects));
    Equal(QueueStatus.Full, e.Enqueue(Inspector.NearbyObjects, new(61)));
    Equal(0, s.ContextReads); Equal(0, s.Reads.Count);
    e.Tick(); Equal(1, e.View().Results.Length); // at most one capture step per tick
}
static void LoggedOutContext()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { LoggedIn = false } };
    using var e = Capture(s, Inspector.Context); var r = Last(e);
    Equal(Outcome.Success, r.Outcome);
    Check(r.Context is { LoggedIn: false, PlayerAvailable: false, Position: null, JobId: null, Level: null, TerritoryId: null });
}
static void LoadingContext()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { Conditions = new(false, false, false, false, true) } };
    using var e = Capture(s, Inspector.Context);
    Check(Last(e).Context is { PlayerAvailable: false, Position: null, JobId: null, Level: null });
}
static void LoggedOutObjects()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { LoggedIn = false } };
    using var e = Capture(s); Equal(Outcome.Blocked, Last(e).Outcome); Equal(Reason.LoggedOut, Last(e).Reason); Equal(0, s.Reads.Count);
}
static void LoadingObjects()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { Conditions = new(false, false, false, false, true) } };
    using var e = Capture(s); Equal(Reason.Loading, Last(e).Reason); Equal(0, s.Reads.Count);
}
static void MissingPlayer()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { PlayerAvailable = false } };
    using var e = Capture(s); Equal(Outcome.Unavailable, Last(e).Outcome); Equal(Reason.PlayerUnavailable, Last(e).Reason);
}
static void MissingTable()
{
    using var e = Capture(Source(0));
    Equal(Outcome.Unavailable, Last(e).Outcome); Equal(Reason.SourceUnavailable, Last(e).Reason);
}
static void InvalidOrigin()
{
    var s = Source(); s.Context = s.Context with { Data = s.Context.Data with { Position = new(float.NaN, 0, 0) } };
    using var e = Capture(s); Equal(Outcome.Unavailable, Last(e).Outcome); Check(Last(e).Context!.Position == null);
}
static void ContextResult()
{
    using var e = Capture(Source(), Inspector.Context); var r = Last(e);
    Equal(1, r.SchemaVersion); Equal(1, r.Steps); Check(!r.NonAtomic && !r.Partial);
    Equal((uint?)100, r.Context!.TerritoryId); Equal(TimeSpan.Zero, r.CompletedUtc.Offset);
}
static void Filtering()
{
    var s = Source(6);
    s.Items[0] = Npc(10); s.Items[1] = Npc(70); s.Items[2] = Npc(3, 43);
    s.Items[3] = new(ObjectCategory.EventNpc, 42, new(2, 0, 0), false, true);
    using var e = Capture(s, options: new(60, ObjectCategory.BattleNpc, 42));
    var r = Last(e); Equal(1, r.Objects.Length); Equal(10d, r.Objects[0].Distance); Equal(6, r.Examined);
    Equal(2, r.Empty); Equal(0, r.Invalid); Equal(3, r.Filtered);
}
static void Accounted(CaptureResult r) =>
    Equal(r.Examined, r.Matched + r.Empty + r.Invalid + r.Filtered + r.Unavailable);
static void ScanCounters()
{
    var s = Source(10);
    s.Items[1] = Npc() with { Valid = false };
    s.Items[2] = Npc(float.NaN);
    s.Items[3] = Npc() with { Kind = (ObjectCategory)500 };
    s.Items[4] = Npc() with { Kind = ObjectCategory.Any };
    s.Items[5] = Npc(100); // radius filtered
    s.Items[6] = Npc() with { Kind = ObjectCategory.EventNpc }; // kind filtered
    s.Items[7] = Npc(id: 43); // base-ID filtered
    s.Items[8] = Npc();
    s.OnRead = i => { if (i == 9) throw new Exception("private data must not be retained"); };
    using var e = Capture(s, options: new(60, ObjectCategory.BattleNpc, 42));
    var r = Last(e);
    Equal(1, r.Empty); Equal(4, r.Invalid); Equal(3, r.Filtered); Equal(1, r.Unavailable); Equal(1, r.Matched);
    Equal(8, r.Skipped); Accounted(r);
    var text = ReportCodec.Describe(r, e.View().Generation);
    Check(text.Contains("empty 1 | invalid 4 | filtered 3 | unreadable 1"));
    Check(!text.Contains("skipped/empty"));
}
static void PartialCounters()
{
    var s = Source(2000); s.Items[0] = Npc();
    using (var e = Capture(s, limits: new(Entries: 4)))
    {
        var r = Last(e); Accounted(r); Equal(3, r.Empty); Equal(Reason.EntryLimit, r.Reason);
    }
    using var cancelled = new DiagnosticEngine(s, new FakeClock());
    cancelled.Enqueue(Inspector.NearbyObjects); cancelled.Tick(); cancelled.Cancel();
    Accounted(Last(cancelled)); Equal(Outcome.Cancelled, Last(cancelled).Outcome);
}
static void NativeKindDetails()
{
    var s = Source(1);
    s.Items[0] = new(ObjectCategory.Other, 12345, new(1, 0, 0), false, true, 6, NativeKinds.Label("GatheringPoint"));
    using var e = Capture(s); var r = Last(e); var row = r.Objects[0];
    Equal(ObjectCategory.Other, row.Kind); Equal((byte?)6, row.RawKindValue);
    Equal(NativeKindLabel.GatheringPoint, row.RawKindLabel); Check(row.BaseId == null);
    Check(ReportCodec.Describe(r, e.View().Generation).Contains("Other | name unavailable | object kind GatheringPoint (6)"));
    Check(typeof(ObjectRow).GetProperties().Where(p => p.Name != nameof(ObjectRow.Name)).All(p => p.PropertyType != typeof(string)));
    Equal(NativeKindLabel.Unknown, NativeKinds.Label("Alice Example token=secret /private/path\n"));
    Equal(NativeKindLabel.Unknown, NativeKinds.Label(null));
}
static void UnknownNativeKind()
{
    var s = Source(2);
    s.Items[0] = new(ObjectCategory.Other, 42, new(1, 0, 0), false, true, 250, NativeKinds.Label("FutureSdkKind"));
    s.Items[1] = new(ObjectCategory.Other, 42, new(1, 0, 0), false, true, 251, (NativeKindLabel)900);
    using var e = Capture(s); var rows = Last(e).Objects;
    Equal(2, rows.Length); Check(rows.All(r => r.RawKindLabel == NativeKindLabel.Unknown && r.BaseId == null));
    Equal((byte?)250, rows[0].RawKindValue); Equal((byte?)251, rows[1].RawKindValue);
}
static void ObjectReportSchema()
{
    var s = Source(2);
    s.Items[0] = new(ObjectCategory.Other, null, new(1, 0, 0), false, true, 6, NativeKindLabel.GatheringPoint, "Mineral Deposit");
    using var e = Capture(s); Equal(3, Last(e).SchemaVersion);
    var preview = ReportCodec.Create(e.View(), Provenance.Create("0.1.1.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
    using var json = JsonDocument.Parse(preview.Content);
    var result = json.RootElement.GetProperty("Results")[0];
    Equal(3, result.GetProperty("SchemaVersion").GetInt32());
    Equal(1, result.GetProperty("Empty").GetInt32()); Equal(0, result.GetProperty("Invalid").GetInt32());
    Equal(0, result.GetProperty("Filtered").GetInt32()); Equal(1, result.GetProperty("Skipped").GetInt32());
    var row = result.GetProperty("Objects")[0];
    Equal(6, row.GetProperty("RawKindValue").GetInt32()); Equal("GatheringPoint", row.GetProperty("RawKindLabel").GetString());
    Equal("Mineral Deposit", row.GetProperty("Name").GetString());
    var text = ReportCodec.Create(e.View(), Provenance.Create("0.1.1.0", "15.0.3.9"), ReportFormat.Text, DateTimeOffset.UtcNow).Content;
    Check(text.Contains("objects.nearby v3") && text.Contains("name Mineral Deposit") && text.Contains("GatheringPoint (6)") && text.Contains("empty 1 | invalid 0 | filtered 0"));
}
static void DisplayNames()
{
    var s = Source(4);
    s.Items[0] = Npc() with { Name = "Training Dummy" };
    s.Items[1] = new(ObjectCategory.Player, uint.MaxValue, new(1, 0, 0), true, true, 1, NativeKindLabel.Pc, "Example Player");
    s.Items[2] = new(ObjectCategory.Companion, 42, new(2, 0, 0), true, true, 9, NativeKindLabel.Companion, "Custom Companion");
    s.Items[3] = new(ObjectCategory.Other, uint.MaxValue, new(3, 0, 0), true, true, 10, NativeKindLabel.Retainer, "Example Retainer");
    using var e = Capture(s); var r = Last(e);
    Equal("Training Dummy", r.Objects[0].Name); Equal("Example Player", r.Objects[1].Name);
    Equal("Custom Companion", r.Objects[2].Name); Equal("Example Retainer", r.Objects[3].Name);
    Check(r.Objects[1].BaseId == null && r.Objects[3].BaseId == null);
    foreach (var format in Enum.GetValues<ReportFormat>())
    {
        var report = ReportCodec.Create(e.View(), Provenance.Create("0.1.3.0", "15.0.3.9"), format, DateTimeOffset.UtcNow).Content;
        Check(report.Contains("Training Dummy") && report.Contains("Example Player") && report.Contains("Custom Companion") && report.Contains("Example Retainer"));
    }
}
static void DisplayNameBounds()
{
    var s = Source(2);
    s.Items[0] = Npc() with { Name = "  Étoile\r\n名\t|\"\u202E test\0  " };
    s.Items[1] = Npc() with { Name = new string('A', 5000) };
    using var e = Capture(s); var names = Last(e).Objects.Select(r => r.Name!).ToArray();
    Check(names[0].Contains("Étoile") && names[0].Contains("名") && names[0].Contains("test"));
    Check(!names[0].Any(char.IsControl) && !names[0].Contains('\u202E') && !names[0].Contains('|') && !names[0].Contains('"'));
    Equal(ObjectNames.MaximumLength, names[1].Length);
    Check(ObjectNames.For(ObjectCategory.EventNpc, "\r\n\t") == null);
    Check(ObjectNames.For((ObjectCategory)999, "Do not retain") == null);
    Check(ObjectNames.For(ObjectCategory.Any, "Do not retain") == null);
    var emoji = ObjectNames.For(ObjectCategory.EventNpc, string.Concat(Enumerable.Repeat("🐈", 200)))!;
    Check(emoji.Length <= ObjectNames.MaximumLength && !char.IsHighSurrogate(emoji[^1]));
}
static void MissingNames()
{
    var s = Source(2); s.Items[0] = Npc(); s.Items[1] = Npc() with { Name = "  " };
    using var e = Capture(s); Equal(2, Last(e).Objects.Length); Check(Last(e).Objects.All(r => r.Name == null));
    Check(ReportCodec.Describe(Last(e), e.View().Generation).Contains("name unavailable"));
}
static void NamedPayloadLimit()
{
    var s = Source(128);
    for (var i = 0; i < 128; i++) s.Items[i] = Npc() with { Name = new string('名', 500) };
    using var e = Capture(s, limits: new(ResultBytes: 4096)); var r = Last(e);
    Check(ReportCodec.ResultSize(r) <= 4096 && r.Omitted > 0 && r.Partial);
    Equal(Reason.PayloadLimit, r.Reason); Equal(r.Matched, r.Objects.Length + r.Omitted);
    Check(r.Objects.All(row => row.Name!.Length == ObjectNames.MaximumLength));
}
static void Incremental()
{
    var s = Source(80); s.Items[79] = Npc();
    using var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects);
    e.Tick(); Equal(32, s.Reads.Count); Equal(0, e.View().Results.Length);
    Equal(QueueStatus.Duplicate, e.Enqueue(Inspector.NearbyObjects));
    Drain(e); var r = Last(e);
    Equal(80, s.Reads.Count); Check(s.Reads.SequenceEqual(Enumerable.Range(0, 80))); Equal(3, r.Steps); Check(r.NonAtomic);
}
static void TimeBudget()
{
    var clock = new FakeClock(); var s = Source(20); s.OnRead = _ => clock.Milliseconds += 3;
    using var e = new DiagnosticEngine(s, clock); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    Equal(1, s.Reads.Count); Drain(e); Equal(20, Last(e).Steps);
}
static void RowCap()
{
    var s = Source(200); for (var i = 0; i < 200; i++) s.Items[i] = Npc();
    using var e = Capture(s); var r = Last(e); Equal(128, r.Objects.Length); Equal(128, r.Examined);
    Equal(Reason.RowLimit, r.Reason); Check(r.Partial);
}
static void EntryCap()
{
    var s = Source(2000); using var e = Capture(s); var r = Last(e);
    Equal(1024, r.Examined); Equal(Reason.EntryLimit, r.Reason); Check(r.Partial);
}
static void FiniteCoordinates()
{
    var s = Source(4); s.Items[0] = Npc(float.NaN); s.Items[1] = Npc(float.PositiveInfinity); s.Items[2] = Npc();
    using var e = Capture(s); Equal(1, Last(e).Objects.Length); Equal(3, Last(e).Skipped);
}
static void Disappearing()
{
    var s = Source(40); s.Items[0] = Npc();
    using var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    s.Items[0] = null; Drain(e); Equal(1, Last(e).Objects.Length); Equal(40, s.Reads.Count);
}
static void UnreadableObjects()
{
    var s = Source(); s.OnRead = i => { if (i == 1) throw new Exception("Secret character path C:\\private token=123"); };
    using var e = Capture(s); var r = Last(e); Equal(Outcome.Unavailable, r.Outcome); Equal(1, r.Unavailable); Check(r.Partial);
}
static void CharacterChange()
{
    var s = Source(80); using var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    s.Context = s.Context with { CharacterToken = 999 }; e.Tick(); Equal(Outcome.Cancelled, Last(e).Outcome);
    Check(Last(e).Generation != e.View().Generation); Equal(32, s.Reads.Count);
}
static void TerritoryChange()
{
    var s = Source(); using var e = Capture(s, Inspector.Context);
    e.Enqueue(Inspector.NearbyObjects); s.Context = s.Context with { Data = s.Context.Data with { TerritoryId = 101 } };
    e.Tick(); Equal(Outcome.Cancelled, Last(e).Outcome); Check(e.View().Results.All(r => r.Generation != e.View().Generation));
    e.Enqueue(Inspector.Context); Drain(e); Equal((uint?)101, Last(e).Context!.TerritoryId);
}
static void AvailabilityChange()
{
    var s = Source(80); using var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    s.Context = s.Context with { Data = s.Context.Data with { PlayerAvailable = false } };
    e.Tick(); Equal(Outcome.Cancelled, Last(e).Outcome);
}
static void MidStepChange()
{
    var s = Source(); s.OnRead = i => { if (i == 0) s.Context = s.Context with { CharacterToken = 500 }; };
    using var e = Capture(s); Equal(Outcome.Cancelled, Last(e).Outcome); Check(Last(e).Generation != e.View().Generation);
}
static void ExplicitInvalidation()
{
    using var e = new DiagnosticEngine(Source(), new FakeClock()); e.Enqueue(Inspector.Context); e.Invalidate();
    Equal(0, e.View().Pending); Equal(Reason.ContextChanged, Last(e).Reason);
}
static void Cancellation()
{
    var s = Source(80); using var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    e.Enqueue(Inspector.Context); e.Cancel(); Equal(2, e.View().Results.Length);
    Check(e.View().Results.All(x => x.Outcome == Outcome.Cancelled)); e.Tick(); Equal(32, s.Reads.Count);
}
static void ClearHistory()
{
    using var e = Capture(Source(), Inspector.Context); e.Enqueue(Inspector.NearbyObjects); e.ClearResults();
    Equal(0, e.View().Results.Length); Equal(1, e.View().Pending); Drain(e); Equal(1, e.View().Results.Length);
}
static void LastSuccess()
{
    var s = Source(); using var e = Capture(s, Inspector.Context); s.FailContext = true; e.Enqueue(Inspector.Context); e.Tick();
    Equal(2, e.View().Results.Length); Equal(Outcome.Unavailable, Last(e).Outcome);
    Equal(Outcome.Success, e.View().Results[0].Outcome); Check(e.View().Results[0].Generation != e.View().Generation);
}
static void HistoryCap()
{
    using var e = new DiagnosticEngine(Source(), new FakeClock(), new(Results: 2));
    for (var i = 0; i < 5; i++) { e.Enqueue(Inspector.Context); Drain(e); }
    Equal(2, e.View().Results.Length); Equal(3L, e.View().Evicted);
}
static void ResultByteCap()
{
    var s = Source(128); for (var i = 0; i < 128; i++) s.Items[i] = Npc();
    using var e = Capture(s, limits: new(ResultBytes: 2048)); var r = Last(e);
    Check(ReportCodec.ResultSize(r) <= 2048); Check(r.Omitted > 0 && r.Partial); Equal(Reason.PayloadLimit, r.Reason);
    Equal(r.Matched, r.Objects.Length + r.Omitted);
}
static void SessionByteCap()
{
    using var e = new DiagnosticEngine(Source(), new FakeClock(), new(ResultBytes: 2048, SessionBytes: 2048));
    for (var i = 0; i < 10; i++) { e.Enqueue(Inspector.Context); Drain(e); }
    Check(e.View().Evicted > 0); Check(e.View().Results.Sum(ReportCodec.ResultSize) <= 2048);
}
static void InvalidLimits()
{
    foreach (var bad in new[] { new Limits(Queue: 17), new Limits(Rows: 129), new Limits(EntriesPerStep: 33),
        new Limits(Entries: 1025), new Limits(Results: 101), new Limits(ResultBytes: 1000),
        new Limits(SessionBytes: 1000), new Limits(StepMilliseconds: double.NaN), new Limits(StepMilliseconds: 3) })
    {
        var rejected = false;
        try { using var e = new DiagnosticEngine(Source(), limits: bad); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Unsafe limit accepted");
    }
}
static void Privacy()
{
    const string secret = "Alice Example API_KEY=supersecret C:\\Users\\Alice\nBearer abc123";
    var s = Source(); s.Context = s.Context with { CharacterToken = 1234567890123456789 };
    s.OnRead = _ => throw new Exception(secret);
    using var e = Capture(s);
    foreach (var format in Enum.GetValues<ReportFormat>())
    {
        var text = ReportCodec.Create(e.View(), Provenance.Create(secret, secret), format, DateTimeOffset.UtcNow).Content;
        foreach (var forbidden in new[] { "Alice", "supersecret", "Bearer", "abc123", "1234567890123456789", "C:\\Users" })
            Check(!text.Contains(forbidden, StringComparison.Ordinal), $"Leaked {forbidden}");
    }
    // Display names are the explicit exception; input/provider exception text still has no retention route.
    Check(typeof(ObjectSample).GetProperties().Where(p => p.Name != nameof(ObjectSample.Name)).All(p => p.PropertyType != typeof(string)));
    Check(typeof(ContextData).GetProperties().All(p => p.PropertyType != typeof(string)));
    s.FailContext = true; e.Enqueue(Inspector.Context); e.Tick();
    Equal(Reason.SourceUnavailable, Last(e).Reason);
}
static void IdentityFields()
{
    var s = Source(); s.Items[0] = new(ObjectCategory.Player, uint.MaxValue, new(1, 0, 0), true, true);
    s.Items[1] = new(ObjectCategory.Other, uint.MaxValue, new(1, 0, 0), true, true);
    using var e = Capture(s); Check(Last(e).Objects.All(x => x.BaseId == null));
}
static void VersionPrivacy()
{
    var p = Provenance.Create("0.1.0.0", "token=secret /home/private");
    Equal("0.1.0.0", p.PluginVersion); Equal("Unavailable", p.DalamudVersion);
}
static void ManifestPrivacy()
{
    using var e = Capture(Source(), Inspector.Context);
    var p = new Provenance("C:\\Users\\Alice", "Bearer secret", "private prompt");
    var text = ReportCodec.Create(e.View(), p, ReportFormat.Json, DateTimeOffset.UtcNow).Content;
    Check(!text.Contains("Alice") && !text.Contains("Bearer") && !text.Contains("private prompt"));
}
static void ReportFormats()
{
    using var e = Capture(Source(), Inspector.Context);
    var p = Provenance.Create("0.1.0.0", "15.0.3.9");
    var json = ReportCodec.Create(e.View(), p, ReportFormat.Json, DateTimeOffset.UtcNow);
    using var parsed = JsonDocument.Parse(json.Content);
    Equal(2, parsed.RootElement.GetProperty("Manifest").GetProperty("FormatVersion").GetInt32());
    Equal(1, parsed.RootElement.GetProperty("Results").GetArrayLength());
    Equal("game.context", Catalog.Get(Last(e).Inspector).Id);
    var text = ReportCodec.Create(e.View(), p, ReportFormat.Text, DateTimeOffset.UtcNow).Content;
    Check(text.Contains("report format 2") && text.Contains("game.context v1") && text.Contains("UTC"));
}
static void ClockJump()
{
    var clock = new FakeClock(); var s = Source(64); using var e = new DiagnosticEngine(s, clock);
    e.Enqueue(Inspector.NearbyObjects); e.Tick(); clock.Utc = clock.Utc.AddDays(-1); clock.Milliseconds += 10;
    Drain(e); Equal(10d, Last(e).DurationMs); Check(Last(e).CompletedUtc < Last(e).StartedUtc);
}
static void Disposal()
{
    var s = Source(80); var e = new DiagnosticEngine(s, new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
    e.Dispose(); e.Dispose(); var reads = s.ContextReads; e.Tick(); Equal(reads, s.ContextReads);
    Equal(QueueStatus.Disposed, e.Enqueue(Inspector.Context)); Equal(Reason.Disposed, Last(e).Reason);
}
static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "debug-kit-tests-" + Guid.NewGuid().ToString("N"));
static ReportPreview Preview()
{
    using var e = Capture(Source(), Inspector.Context);
    return ReportCodec.Create(e.View(), Provenance.Create("0.1.0.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
}
static void ExportExact()
{
    var dir = TemporaryDirectory(); var preview = Preview();
    try
    {
        using var exporter = new ReportExporter(dir);
        Equal(Path.GetFullPath(dir), exporter.DirectoryPath);
        Equal(ExportStatus.Success, exporter.ExportAsync(preview).GetAwaiter().GetResult());
        Check(File.ReadAllBytes(Path.Combine(dir, preview.FileName)).SequenceEqual(preview.Bytes));
        Equal(ExportStatus.AlreadyExists, exporter.ExportAsync(preview).GetAwaiter().GetResult());
        Equal(1, Directory.GetFiles(dir).Length);
    }
    finally { DeleteOwnTestDirectory(dir); }
}
static void ExportCancellation()
{
    var dir = TemporaryDirectory();
    try
    {
        using var exporter = new ReportExporter(dir); using var cts = new CancellationTokenSource(); cts.Cancel();
        Equal(ExportStatus.Cancelled, exporter.ExportAsync(Preview(), cts.Token).GetAwaiter().GetResult());
        Check(!Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0);
    }
    finally { DeleteOwnTestDirectory(dir); }
}
static void ExportFailure()
{
    var dir = TemporaryDirectory(); Directory.CreateDirectory(dir); var obstruction = Path.Combine(dir, "file-not-directory");
    File.WriteAllText(obstruction, "preserve");
    try
    {
        using var exporter = new ReportExporter(obstruction);
        Equal(ExportStatus.Failed, exporter.ExportAsync(Preview()).GetAwaiter().GetResult());
        Equal("preserve", File.ReadAllText(obstruction)); Equal(1, Directory.GetFiles(dir).Length);
    }
    finally { DeleteOwnTestDirectory(dir); }
}
static void ExportDisposal()
{
    var dir = TemporaryDirectory(); using var exporter = new ReportExporter(dir); exporter.Dispose();
    Equal(ExportStatus.Disposed, exporter.ExportAsync(Preview()).GetAwaiter().GetResult()); Check(!Directory.Exists(dir));
}
static void OversizedExport()
{
    var dir = TemporaryDirectory(); using var exporter = new ReportExporter(dir);
    var huge = new ReportPreview(Guid.NewGuid(), ReportFormat.Text, ImmutableArray.CreateRange(new byte[2 * 1024 * 1024 + 1]));
    Equal(ExportStatus.Failed, exporter.ExportAsync(huge).GetAwaiter().GetResult()); Check(!Directory.Exists(dir));
}
static void DeleteOwnTestDirectory(string dir)
{
    // Delete only files created in this unique test-owned directory; never recurse into arbitrary paths.
    if (!Directory.Exists(dir)) return;
    foreach (var file in Directory.GetFiles(dir)) File.Delete(file);
    Directory.Delete(dir);
}

sealed class FakeClock : IClock
{
    public DateTimeOffset Utc = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    public double Milliseconds;
    public DateTimeOffset UtcNow => Utc;
    public long Timestamp => (long)(Milliseconds * 1000);
    public double ElapsedMilliseconds(long since) => Milliseconds - since / 1000d;
}

sealed class FakeSource(int length) : IGameSource
{
    public LiveContext Context = new(new(true, 100, true, new(0, 0, 0), 19, 100,
        new(false, false, false, false, false)), 123);
    public readonly Dictionary<int, ObjectSample?> Items = new();
    public readonly List<int> Reads = new();
    public int ContextReads;
    public bool FailContext;
    public Action<int>? OnRead;
    public LiveContext ReadContext()
    {
        ContextReads++;
        if (FailContext) throw new Exception("Alice Example /private/path token=secret");
        return Context;
    }
    public int ObjectTableLength => length;
    public ObjectSample? ReadObject(int index)
    {
        Reads.Add(index); OnRead?.Invoke(index); return Items.GetValueOrDefault(index);
    }
}
