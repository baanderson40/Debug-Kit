using System.Collections.Immutable;
using System.Text.Json;
using DebugKit.Core;
using DebugKit.Readers;
using System.Text;

internal static class Phase2Tests
{
    public static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("phase2 descriptors use explicit stable IDs", CatalogIds),
        ("comparison labels identify captured inputs and unique requests", SelectionLabels),
        ("unsupported target fields say unavailable and preserve zero values", TargetUnavailableFields),
        ("profiling defaults off and bounds warmed samples", ProfilingBounds),
        ("profiling clear and disable discard in-flight measurements", ProfilingLifecycle),
        ("profiling measures engine reader serialization formatting and previews", ProfilingIntegration),
        ("menu labels strip SeString formatting without leaking payload bytes", MenuFormatting),
        ("menu labels reject malformed unsupported and oversized data", MenuMalformed),
        ("phase2 rejects invalid addon names and ranges before reads", InputBounds),
        ("phase2 validates sheet language inventory and comparison selections", OtherInputs),
        ("phase2 canonicalizes irrelevant options before deduplication", CanonicalInputs),
        ("target payload retained without nearby scan or fake counters", Target),
        ("no target stays unavailable not empty success", NoTarget),
        ("phase2 missing reader fails explicitly", MissingReader),
        ("addon list pages merge detached rows", AddonPages),
        ("addon closure/lifetime change cancels between pages", LifetimeChange),
        ("phase2 context replacement cancels capture", ContextChange),
        ("phase2 cancellation preserves partial data", CancelJob),
        ("phase2 clear does not silently cancel queued tool", ClearJob),
        ("unloaded inventory remains unavailable across later good pages", UnloadedInventory),
        ("missing sheet row remains unavailable", MissingRow),
        ("typed payloads survive JSON and text export", PayloadSerialization),
        ("generated report JSON matches legacy bytes without implicit reflection", GeneratedJsonParity),
        ("phase2 row caps stop oversized capture", ToolRowCap),
        ("phase2 byte limits trim typed payloads honestly", ToolByteCap),
        ("phase2 stuck reader cannot run forever", NoProgress),
        ("phase2 empty successful payload is not fabricated success", EmptyPayload),
        ("payload text bounds and target nonfinite values are enforced", PayloadSafety),
        ("native profile requires exact game assembly and hash", NativeProfile),
        ("native provenance rejects paths and invalid version strings", BuildProvenance),
        ("native blocked status survives capture and export", NativeBlocked),
        ("node traversal detects cycles", NodeCycles),
        ("node traversal enforces depth and visited budgets", NodeBounds),
        ("inventory readiness distinguishes unloaded from empty", InventoryReadiness),
        ("inventory filters preserve HQ and NQ separation", InventoryFilters),
        ("comparison reports changed field values", ChangedFields),
        ("comparison complete rows distinguish added removed unchanged", CompleteChanges),
        ("comparison partial evidence cannot prove removal", PartialComparison),
        ("comparison rejects schemas inputs and context mismatch", Incompatible),
        ("comparison rejects build language and addon lifetime mismatch", IncompatibleProvenance),
        ("comparison identifies duplicate row keys as ambiguous", AmbiguousRows),
        ("nearby comparison never proves entity removals", ObjectIdentity),
        ("target comparison never claims entity continuity", TargetIdentity),
        ("comparison only reads retained captures", CompareJob),
        ("evicted comparison source returns snapshot unavailable", EvictedSources),
        ("comparison output is bounded", CompareBounds),
    };

    private static void Check(bool condition) { if (!condition) throw new Exception("Phase 2 assertion failed"); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual));
    private static ToolOptions Options(Inspector inspector) => new ToolOptions(Addon: inspector == Inspector.AddonList ? "" : "ContextMenu",
        ExperimentalNative: true).Canonical(inspector);
    private static FieldRow F(string key, string value) => new(key, value);
    private static SheetPayload Sheet(params FieldRow[] fields) => new(SheetKind.Item, 1, "English", fields.ToImmutableArray(), NativeCompatibility.GameBuild);
    private static AddonRow Addon(string name = "Inventory") => new(name, 1, true, true, 10, 20);
    private static TargetPayload TargetData() => new(new(new(ObjectCategory.Player, null, new(1, 2, 3), null, true, 1, NativeKindLabel.Pc, "Example Player"), 1, 2, 100, 200, 19, 100,
        TargetStatusAvailability.Available, [new(123, "Localized status", 12.5f)], 1));
    private static CaptureResult Capture(Inspector inspector, ToolPayload? payload, ToolOptions? options = null, Outcome outcome = Outcome.Success, Reason reason = Reason.None)
    {
        var source = new ToolSource { Read = (_, _, _, _) => new(payload, Outcome: outcome, Reason: reason) };
        using var engine = new DiagnosticEngine(source, new FakeClock());
        Equal(QueueStatus.Accepted, engine.EnqueueTool(inspector, options ?? Options(inspector)));
        engine.Tick(); return engine.View().Results[^1];
    }
    private static void Drain(DiagnosticEngine engine)
    {
        for (var i = 0; i < 150; i++)
        {
            engine.Tick();
            if (engine.View() is { Pending: 0, ActiveRequest: null }) return;
        }
        throw new Exception("Job did not terminate");
    }

    private static void CatalogIds()
    {
        Equal(39, Catalog.All.Select(d => d.Id).Distinct().Count());
        foreach (var descriptor in Catalog.All.Reverse()) Equal(descriptor, Catalog.Get(descriptor.Inspector));
        Equal("objects.target", Catalog.Get(Inspector.Target).Id); Equal(2, Catalog.Get(Inspector.Target).Version);
        Equal("results.compare", Catalog.Get(Inspector.Compare).Id);
    }
    private static void SelectionLabels()
    {
        var sheet = Capture(Inspector.SheetRow, Sheet(F("Name", "ギル")), new(Language: DataLanguage.Japanese));
        var label = SnapshotLabels.For(sheet);
        Check(label.Contains("Item #1 · Japanese") && label.Contains("UTC") && label.Contains("Success"));
        Check(label.Contains(sheet.RequestId.ToString()[..8]));
        Check(label != SnapshotLabels.For(sheet with { RequestId = Guid.NewGuid() }));
        var cases = new (Inspector Inspector, ToolOptions Options, string Expected)[]
        {
            (Inspector.AddonList, new(Scope: AddonScope.Visible, Addon: "Character"), "Visible · filter \"Character\""),
            (Inspector.AddonList, new(Addon: ""), "Loaded · all names"),
            (Inspector.AddonValues, new(Addon: "Character", Instance: 2, Start: 4, Count: 16), "Character [2] · start 4 · count 16"),
            (Inspector.AddonNodes, new(Addon: "Character", NodeId: 1, Depth: 8), "Character [1] · node 1 · depth 8"),
            (Inspector.AddonNodes, new(Addon: "Character"), "all nodes"),
            (Inspector.ContextMenu, new(Instance: 2), "ContextMenu [2]"),
            (Inspector.Inventory, new(Bag: InventoryBag.Bag1, ItemId: 123, Quality: ItemQuality.Hq, IncludeEmpty: true), "Bag1 · item 123 · Hq · include empty"),
            (Inspector.Inventory, new(), "All · all items · Any"),
        };
        foreach (var item in cases)
            Check(SnapshotLabels.For(sheet with { Inspector = item.Inspector, ToolOptions = item.Options }).Contains(item.Expected));
        Check(SnapshotLabels.For(sheet with { Inspector = Inspector.NearbyObjects, Options = new(25, ObjectCategory.BattleNpc, 42) })
            .Contains("radius 25y · BattleNpc · base 42"));
        Check(SnapshotLabels.For(sheet with { Inspector = Inspector.Context, ToolOptions = null }).StartsWith("Game Context · "));
        Check(SnapshotLabels.For(sheet with { Payload = null, Outcome = Outcome.Unavailable }).Contains("Item #1 · Japanese"));
    }
    private static void MenuFormatting()
    {
        Equal("Dye", MenuLabelDecoder.Decode("Dye"u8));
        Equal("ギル", MenuLabelDecoder.Decode(Encoding.UTF8.GetBytes("ギル")));
        // UIColor foreground/edge payloads surround literal text. Payload bytes are not UTF-8 text.
        byte[] decorated = [0x02, 0x48, 0x02, 0x02, 0x03, 0x02, 0x49, 0x02, 0x02, 0x03,
            .. Encoding.UTF8.GetBytes("+ Add to Discard list"),
            0x02, 0x49, 0x02, 0x01, 0x03, 0x02, 0x48, 0x02, 0x01, 0x03];
        Equal("+ Add to Discard list", MenuLabelDecoder.Decode(decorated));
        byte[] icon = [0x02, 0x12, 0x02, 0x02, 0x03, .. "Search in Market Board"u8];
        Equal("Search in Market Board", MenuLabelDecoder.Decode(icon));
        Equal(128, MenuLabelDecoder.Decode(Encoding.UTF8.GetBytes(new string('x', 200)))!.Length);
    }

    private static void MenuMalformed()
    {
        byte[][] invalid = [[], [0xC3, 0x28], [0x02], [0x02, 0x48, 0x02, 0x02],
            [0x02, 0x48, 0x02, 0x02, 0x04], [0x02, 0x48, 0x02, 0xFF, 0x03],
            [.. "Text"u8, 0x00], [.. "Text"u8, 0x03],
            // Valid number macro requires evaluation, not silent omission.
            [0x02, 0x20, 0x02, 0x02, 0x03, .. "Text"u8],
            [0x02, 0x48, 0x02, 0x02, 0x03],
            Encoding.UTF8.GetBytes(new string('x', MenuLabelDecoder.MaxBytes))];
        foreach (var bytes in invalid) Check(MenuLabelDecoder.Decode(bytes) == null);
    }
    private static void TargetUnavailableFields()
    {
        var payload = TargetData();
        payload = payload with { Target = payload.Target with { Hp = null, MaxHp = null, JobId = null, Level = null,
            Rotation = null, HitboxRadius = null, Object = payload.Target.Object with
            { Kind = ObjectCategory.Other, RawKindValue = 5, RawKindLabel = NativeKindLabel.Aetheryte, Name = "aetheryte" } } };
        var result = Capture(Inspector.Target, payload);
        var text = ReportCodec.Describe(result, result.Generation);
        Check(text.Contains("Rotation unavailable | hitbox unavailable | HP unavailable/unavailable | job unavailable | level unavailable"));
        payload = payload with { Target = payload.Target with { Hp = 0, MaxHp = 100, Rotation = 0, HitboxRadius = 0, Level = 0 } };
        result = Capture(Inspector.Target, payload);
        text = ReportCodec.Describe(result, result.Generation);
        Check(text.Contains("Rotation 0 | hitbox 0 | HP 0/100 | job unavailable | level 0"));
        var preview = ReportCodec.Create(new(0, 0, null, 0, [result], 0, 0, 0, true),
            Provenance.Create("0.2.0.0", "15.0.3.9"), ReportFormat.Text, DateTimeOffset.UtcNow);
        Check(preview.Content.Contains("HP 0/100 | job unavailable"));
    }

    private static void ProfilingBounds()
    {
        var recorder = new PerformanceRecorder();
        Check(!recorder.Enabled);
        using (recorder.Measure(Inspector.Target, PerformanceArea.ReaderJob)) { }
        Check(recorder.Snapshot().IsEmpty);
        recorder.Enabled = true;
        using (recorder.Measure(Inspector.Target, PerformanceArea.ReaderJob))
            GC.KeepAlive(new byte[1024]);
        var first = recorder.Snapshot().Single();
        Equal(1L, first.Samples); Equal(0, first.WarmSamples); Check(first.FirstBytes >= 1024);
        Check(PerformanceRecorder.Describe(recorder.Snapshot()).Contains("unavailable (no later samples)"));
        for (var i = 0; i < 300; i++)
            using (recorder.Measure(Inspector.Target, PerformanceArea.ReaderJob)) GC.KeepAlive(new byte[i]);
        var warm = recorder.Snapshot().Single();
        Equal(301L, warm.Samples); Equal(256, warm.WarmSamples);
        Equal(first.FirstMs, warm.FirstMs); Equal(first.FirstBytes, warm.FirstBytes);
        Check(warm.MedianMs >= 0 && warm.MedianMs <= warm.P95Ms && warm.P95Ms <= warm.MaxMs);
        Check(warm.MedianBytes >= 0 && warm.MedianBytes <= warm.P95Bytes && warm.P95Bytes <= warm.MaxBytes);
        using (recorder.Measure((Inspector)500, PerformanceArea.ReaderJob)) { }
        using (recorder.Measure(null, (PerformanceArea)500)) { }
        Equal(1, recorder.Snapshot().Length);
    }

    private static void ProfilingLifecycle()
    {
        var recorder = new PerformanceRecorder { Enabled = true };
        using (recorder.Measure(null, PerformanceArea.WindowDraw)) recorder.Clear();
        Check(recorder.Snapshot().IsEmpty);
        using (recorder.Measure(null, PerformanceArea.WindowDraw)) recorder.Enabled = false;
        Check(recorder.Snapshot().IsEmpty);
        recorder.Enabled = true;
        using (recorder.Measure(null, PerformanceArea.WindowDraw)) { }
        Equal(1L, recorder.Snapshot().Single().Samples);
        recorder.Enabled = false;
        using (recorder.Measure(null, PerformanceArea.WindowDraw)) { }
        Equal(1L, recorder.Snapshot().Single().Samples);
        recorder.Clear(); Check(recorder.Snapshot().IsEmpty);
    }

    private static void ProfilingIntegration()
    {
        using var engine = new DiagnosticEngine(new ToolSource { Read = (_, _, _, _) => new(Sheet(F("Name", "Gil"))) }, new FakeClock());
        engine.Performance.Enabled = true;
        engine.EnqueueTool(Inspector.SheetRow, Options(Inspector.SheetRow)); engine.Tick();
        var result = engine.View().Results.Single();
        Check(ReportCodec.Describe(result, 0, engine.Performance).Contains("Capture span:"));
        foreach (var format in Enum.GetValues<ReportFormat>())
            ReportCodec.Create(engine.View(), Provenance.Create("0.2.0.0", "15.0.3.9"), format, DateTimeOffset.UtcNow, engine.Performance);
        var rows = engine.Performance.Snapshot();
        foreach (var area in new[] { PerformanceArea.FrameworkStep, PerformanceArea.ContextRead, PerformanceArea.ReaderJob,
            PerformanceArea.JsonSerialization, PerformanceArea.TextFormatting, PerformanceArea.PreviewPreparation })
            Check(rows.Any(r => r.Inspector == Inspector.SheetRow && r.Area == area));
        // Profiling never becomes report payload/history or changes capture outcome.
        Equal(Outcome.Success, result.Outcome);
        var json = ReportCodec.Create(engine.View(), Provenance.Create("0.2.0.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow).Content;
        Check(!json.Contains("FirstBytes") && !json.Contains("PerformanceSummary"));
    }
    private static void InputBounds()
    {
        var source = new ToolSource(); using var engine = new DiagnosticEngine(source);
        foreach (var options in new[] { new ToolOptions(Addon: "../private"), new ToolOptions(Addon: "ContextMenu\n"),
            new ToolOptions(Instance: 0), new ToolOptions(Instance: 9), new ToolOptions(Start: -1), new ToolOptions(Count: 129),
            new ToolOptions(Start: 4095, Count: 2), new ToolOptions(Addon: new string('a', 65)) })
            Equal(QueueStatus.Invalid, engine.EnqueueTool(Inspector.AddonValues, options));
        Equal(QueueStatus.Invalid, engine.EnqueueTool(Inspector.AddonNodes, new(Depth: 9)));
        Equal(0, source.ToolReads); Equal(0, source.ContextReads);
    }
    private static void OtherInputs()
    {
        Check(!new ToolOptions(Sheet: (SheetKind)500).IsValid(Inspector.SheetRow));
        Check(!new ToolOptions(Language: (DataLanguage)500).IsValid(Inspector.SheetRow));
        Check(!new ToolOptions(Bag: (InventoryBag)500).IsValid(Inspector.Inventory));
        Check(!new ToolOptions(ItemId: 0).IsValid(Inspector.Inventory));
        Check(!new ToolOptions().IsValid(Inspector.Compare));
        var id = Guid.NewGuid(); Check(!new ToolOptions(Baseline: id, Comparison: id).IsValid(Inspector.Compare));
        Check(new ToolOptions(RowId: 0).IsValid(Inspector.SheetRow));
    }
    private static void CanonicalInputs()
    {
        using var engine = new DiagnosticEngine(new ToolSource());
        Equal(QueueStatus.Accepted, engine.EnqueueTool(Inspector.Target, new(Sheet: SheetKind.Mount, Addon: "Inventory")));
        Equal(QueueStatus.Duplicate, engine.EnqueueTool(Inspector.Target, new()));
    }
    private static void Target()
    {
        var result = Capture(Inspector.Target, TargetData());
        Check(result.Payload is TargetPayload); Equal(Outcome.Success, result.Outcome); Equal(1, result.Steps);
        var text = ReportCodec.Describe(result, result.Generation);
        Check(text.Contains("Example Player") && text.Contains("distance unavailable") && text.Contains("Localized status") && text.Contains("remaining 12.5s") && !text.Contains("empty 0"));
    }
    private static void NoTarget()
    {
        var result = Capture(Inspector.Target, null, outcome: Outcome.Unavailable, reason: Reason.NoTarget);
        Equal(Reason.NoTarget, result.Reason); Equal(Outcome.Unavailable, result.Outcome);
    }
    private static void MissingReader()
    {
        using var engine = new DiagnosticEngine(new FakeSource(4), new FakeClock());
        engine.EnqueueTool(Inspector.Target, new()); engine.Tick(); Equal(Reason.SourceUnavailable, engine.View().Results[^1].Reason);
    }
    private static void AddonPages()
    {
        var source = new ToolSource { Read = (_, _, cursor, _) => new(new AddonsPayload([Addon(cursor.ToString())], NativeCompatibility.GameBuild, "ExperimentalMatched"), cursor + 1, cursor == 2, SourceStamp: 3, Examined: 1, Matched: 1) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); Drain(engine);
        var result = engine.View().Results[^1]; Equal(3, ((AddonsPayload)result.Payload!).Rows.Length); Equal(3, result.Steps); Check(result.NonAtomic);
    }
    private static void LifetimeChange()
    {
        var source = new ToolSource { Read = (_, _, cursor, _) => new(new AddonsPayload([Addon()], NativeCompatibility.GameBuild, "ExperimentalMatched"), cursor + 1, false, SourceStamp: cursor + 1, Examined: 1) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); engine.Tick(); engine.Tick();
        var result = engine.View().Results[^1]; Equal(Outcome.Cancelled, result.Outcome); Equal(Reason.SourceChanged, result.Reason);
    }
    private static void ContextChange()
    {
        var source = new ToolSource { Read = (_, _, c, _) => new(new AddonsPayload([], NativeCompatibility.GameBuild, "ExperimentalMatched"), c + 1, false) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); engine.Tick();
        source.Context = source.Context with { CharacterToken = 321 }; engine.Tick(); Equal(Reason.ContextChanged, engine.View().Results[^1].Reason);
    }
    private static void CancelJob()
    {
        var source = new ToolSource { Read = (_, _, c, _) => new(new AddonsPayload([Addon()], NativeCompatibility.GameBuild, "ExperimentalMatched"), c + 1, false) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); engine.Tick(); engine.Cancel();
        Check(engine.View().Results[^1] is { Partial: true, Outcome: Outcome.Cancelled, Payload: AddonsPayload });
    }
    private static void ClearJob()
    {
        using var engine = new DiagnosticEngine(new ToolSource { Read = (_, _, _, _) => new(TargetData()) }, new FakeClock());
        engine.EnqueueTool(Inspector.Target, new()); engine.ClearResults(); engine.Tick(); Equal(1, engine.View().Results.Length);
    }
    private static void UnloadedInventory()
    {
        var source = new ToolSource { Read = (_, _, c, _) => c == 0
            ? new(new InventoryPayload([], NativeCompatibility.GameBuild), 256, false, true, Reason: Reason.ContainerUnavailable, Unavailable: 1)
            : new(new InventoryPayload([new(InventoryBag.Bag2, 0, 1, "Item", 2, true)], NativeCompatibility.GameBuild), 1024, Examined: 1, Matched: 1) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.Inventory, Options(Inspector.Inventory)); Drain(engine);
        var result = engine.View().Results[^1]; Equal(Outcome.Unavailable, result.Outcome); Equal(Reason.ContainerUnavailable, result.Reason); Check(result.Partial);
    }
    private static void MissingRow() => Equal(Reason.RowNotFound, Capture(Inspector.SheetRow, null, outcome: Outcome.Unavailable, reason: Reason.RowNotFound).Reason);
    private static void PayloadSerialization()
    {
        ToolPayload[] payloads = [ TargetData(), new AddonsPayload([Addon()], NativeCompatibility.GameBuild, "ExperimentalMatched"),
            new ValuesPayload("ContextMenu", 1, 0, [new(0, "UInt", "2", true)], NativeCompatibility.GameBuild),
            new NodesPayload("Inventory", 1, 0, [new(0, 1, 3, 0, true, 1, 2, 100, 100, false)], NativeCompatibility.GameBuild),
            new MenuPayload(1, 0, [new(0, "String", "Examine", true)], NativeCompatibility.GameBuild), Sheet(F("Name", "Potion")),
            new InventoryPayload([new(InventoryBag.Bag1, 0, 1, "Potion", 3, false)], NativeCompatibility.GameBuild) ];
        Inspector[] inspectors = [Inspector.Target, Inspector.AddonList, Inspector.AddonValues, Inspector.AddonNodes, Inspector.ContextMenu, Inspector.SheetRow, Inspector.Inventory];
        for (var i = 0; i < payloads.Length; i++)
        {
            var result = Capture(inspectors[i], payloads[i]);
            var view = new EngineView(0, 0, null, 0, [result], 0, 0, 0, true);
            foreach (var format in Enum.GetValues<ReportFormat>())
            {
                var content = ReportCodec.Create(view, Provenance.Create("0.2.0.0", "15.0.3.9", NativeCompatibility.GameBuild), format, DateTimeOffset.UtcNow).Content;
                Check(content.Contains(Catalog.Get(inspectors[i]).Id) || format == ReportFormat.Json);
                if (format == ReportFormat.Json)
                {
                    using var json = JsonDocument.Parse(content);
                    Check(json.RootElement.GetProperty("Results")[0].GetProperty("Payload").TryGetProperty("$type", out _));
                }
            }
        }
    }
    private static void GeneratedJsonParity()
    {
        Check(!JsonSerializer.IsReflectionEnabledByDefault);
        // Explicit reflection is a test-only compatibility oracle; production uses generated type info exclusively.
        var legacy = new JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        ToolPayload[] payloads = [TargetData(), new AddonsPayload([Addon()], NativeCompatibility.GameBuild, "ExperimentalMatched"),
            new ValuesPayload("Character", 1, 2, [new(5, "Bool", "False", true)], NativeCompatibility.GameBuild),
            new NodesPayload("Character", 1, 2, [new(0, 1, 1, 0, true, 590, 178, 736, 622, false)], NativeCompatibility.GameBuild),
            new MenuPayload(1, 2, [new(0, "String", "ギル <&> \"Label\"", true)], NativeCompatibility.GameBuild),
            Sheet(F("Name", "ギル")), new InventoryPayload([new(InventoryBag.Bag1, 0, 10373, "Magitek Repair Materials", 87, false)], NativeCompatibility.GameBuild),
            new ComparisonPayload(Guid.NewGuid(), Guid.NewGuid(), [new("node/1", ChangeKind.Changed, "x=1", "x=2")], 0, 0, 1, 0, 0, 0, true)];
        Inspector[] inspectors = [Inspector.Target, Inspector.AddonList, Inspector.AddonValues, Inspector.AddonNodes,
            Inspector.ContextMenu, Inspector.SheetRow, Inspector.Inventory, Inspector.Compare];
        using var engine = new DiagnosticEngine(new FakeSource(1), new FakeClock());
        engine.Enqueue(Inspector.Context); engine.Tick();
        engine.Enqueue(Inspector.NearbyObjects); engine.Tick();
        var results = engine.View().Results.ToBuilder();
        var template = results[0];
        for (var i = 0; i < payloads.Length; i++)
            results.Add(template with { RequestId = Guid.NewGuid(), Inspector = inspectors[i], SchemaVersion = 1,
                ToolOptions = Options(inspectors[i]), Payload = payloads[i] });
        foreach (var result in results)
        {
            var expected = JsonSerializer.SerializeToUtf8Bytes(result, legacy);
            Equal(expected.Length, ReportCodec.ResultSize(result));
            var view = engine.View() with { Results = [result] };
            var preview = ReportCodec.Create(view, Provenance.Create("0.2.0.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
            var report = JsonSerializer.Deserialize<DiagnosticReport>(preview.Bytes.AsSpan(), legacy)!;
            Check(preview.Bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(report, legacy)));
        }
    }
    private static void ToolRowCap()
    {
        var source = new ToolSource { Read = (_, _, c, _) => new(new AddonsPayload(Enumerable.Range(0, 32).Select(i => Addon($"A{c + i}")).ToImmutableArray(), NativeCompatibility.GameBuild, "ExperimentalMatched"), c + 32, false, Examined: 32) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); Drain(engine);
        var r = engine.View().Results[^1]; Equal(256, Payloads.Count(r.Payload)); Equal(Reason.RowLimit, r.Reason); Check(r.Partial);
    }
    private static void ToolByteCap()
    {
        using var engine = new DiagnosticEngine(new ToolSource { Read = (_, _, _, _) => new(Sheet(Enumerable.Range(0, 64).Select(i => F(i.ToString(), new string('名', 128))).ToArray())) }, new FakeClock(), new(ResultBytes: 2048));
        engine.EnqueueTool(Inspector.SheetRow, Options(Inspector.SheetRow)); engine.Tick(); var r = engine.View().Results[^1];
        Check(ReportCodec.ResultSize(r) <= 2048 && r.Partial && r.Omitted > 0);
    }
    private static void NoProgress()
    {
        var source = new ToolSource { Read = (_, _, c, _) => new(new AddonsPayload([], NativeCompatibility.GameBuild, "ExperimentalMatched"), c, false) };
        using var engine = new DiagnosticEngine(source, new FakeClock()); engine.EnqueueTool(Inspector.AddonList, Options(Inspector.AddonList)); engine.Tick();
        Equal(Reason.EntryLimit, engine.View().Results[^1].Reason);
    }
    private static void EmptyPayload() => Equal(Outcome.Unavailable, Capture(Inspector.Target, null).Outcome);
    private static void PayloadSafety()
    {
        var target = TargetData(); target = target with { Target = target.Target with { Rotation = float.NaN,
            Object = target.Target.Object with { Name = new string('名', 1000) + "\n", Distance = double.NaN, BaseId = uint.MaxValue } } };
        var result = Capture(Inspector.Target, target); var clean = ((TargetPayload)result.Payload!).Target;
        Check(clean.Rotation == null && clean.Object.Distance == null && clean.Object.BaseId == null);
        Equal(128, clean.Object.Name!.Length);
        Equal(TargetStatusAvailability.Available, clean.StatusAvailability);
        Equal(123u, clean.Statuses[0].StatusId);
        Equal(12.5f, clean.Statuses[0].RemainingTime);
        var bad = (TargetPayload)Payloads.Sanitize(target with { Target = target.Target with
        {
            StatusAvailability = (TargetStatusAvailability)999,
            Statuses = Enumerable.Range(0, 100).Select(i => new TargetStatusData((uint)i, new string('x', 300), float.NaN)).ToImmutableArray(),
            StatusCount = 100,
        } })!;
        Equal(TargetStatusAvailability.Failed, bad.Target.StatusAvailability);
        Equal(80, bad.Target.Statuses.Length);
        Check(bad.Target.StatusesTruncated && bad.Target.Statuses.All(s => s.RemainingTime == null && s.Name!.Length <= 128));
    }
    private static void NativeProfile()
    {
        Check(NativeCompatibility.Matches(NativeCompatibility.GameBuild, NativeCompatibility.DalamudVersion, NativeCompatibility.StructsHash));
        Check(!NativeCompatibility.Matches("2026.11.01.0000.0000", NativeCompatibility.DalamudVersion, NativeCompatibility.StructsHash));
        Check(!NativeCompatibility.Matches(NativeCompatibility.GameBuild, "16.0.0.0", NativeCompatibility.StructsHash));
        Check(!NativeCompatibility.Matches(NativeCompatibility.GameBuild, NativeCompatibility.DalamudVersion, "wrong hash"));
        Check(!NativeCompatibility.Matches(null, null, null));
    }
    private static void BuildProvenance()
    {
        Equal("Unavailable", NativeCompatibility.SafeBuild("C:\\private\\path"));
        Equal("Unavailable", NativeCompatibility.SafeBuild(NativeCompatibility.GameBuild + "\n"));
        var p = Provenance.Create("0.2.0.0", "15.0.3.9", NativeCompatibility.GameBuild, "arbitrary secret");
        Equal(NativeCompatibility.GameBuild, p.GameBuild); Equal("Unsupported", p.NativeProfile);
    }
    private static void NativeBlocked()
    {
        var r = Capture(Inspector.AddonNodes, null, outcome: Outcome.Unavailable, reason: Reason.UnsupportedBuild);
        Equal(Reason.UnsupportedBuild, r.Reason); Check(ReportCodec.Describe(r, 0).Contains("UnsupportedBuild"));
    }
    private static void NodeCycles()
    {
        var guard = new TraversalGuard<int>(); Check(guard.TryVisit(1, 0)); Check(!guard.TryVisit(1, 1)); Check(guard.Cycle && guard.Limited);
    }
    private static void NodeBounds()
    {
        var depth = new TraversalGuard<int>(2); Check(depth.TryVisit(1, 0)); Check(!depth.TryVisit(2, 3));
        var nodes = new TraversalGuard<int>(8, 2); Check(nodes.TryVisit(1, 0)); Check(nodes.TryVisit(2, 0)); Check(!nodes.TryVisit(3, 0)); Equal(2, nodes.Count);
    }
    private static void InventoryReadiness()
    {
        Check(InventoryPolicy.Ready(true, true, 35)); Check(!InventoryPolicy.Ready(false, true, 35));
        Check(!InventoryPolicy.Ready(true, false, 35)); Check(!InventoryPolicy.Ready(true, true, -1)); Check(!InventoryPolicy.Ready(true, true, 257));
    }
    private static void InventoryFilters()
    {
        Check(InventoryPolicy.Matches(new(ItemId: 1, Quality: ItemQuality.Hq), 1, true));
        Check(!InventoryPolicy.Matches(new(ItemId: 1, Quality: ItemQuality.Hq), 1, false));
        Check(!InventoryPolicy.Matches(new(ItemId: 1), 2, true)); Check(InventoryPolicy.Matches(new(Quality: ItemQuality.Nq), 2, false));
    }
    private static void ChangedFields()
    {
        var a = Capture(Inspector.SheetRow, Sheet(F("Name", "A"))); var b = Capture(Inspector.SheetRow, Sheet(F("Name", "B")));
        var p = (ComparisonPayload)SnapshotComparer.Compare(a, b, false).Payload!; Equal(1, p.Changed); Equal("A", p.Rows[0].Before); Equal("B", p.Rows[0].After);
    }
    private static void CompleteChanges()
    {
        var a = Capture(Inspector.SheetRow, Sheet(F("Same", "1"), F("Removed", "2")));
        var b = Capture(Inspector.SheetRow, Sheet(F("Same", "1"), F("Added", "3")));
        var p = (ComparisonPayload)SnapshotComparer.Compare(a, b, true).Payload!; Equal(1, p.Added); Equal(1, p.Removed); Equal(1, p.Unchanged); Equal(2, p.Rows.Length);
    }
    private static void PartialComparison()
    {
        var a = Capture(Inspector.SheetRow, Sheet(F("Name", "A"))); var b = Capture(Inspector.SheetRow, Sheet()) with { Partial = true };
        var p = (ComparisonPayload)SnapshotComparer.Compare(a, b, false).Payload!; Equal(0, p.Removed); Equal(1, p.Unobserved); Check(!p.CompleteEvidence);
    }
    private static void Incompatible()
    {
        var a = Capture(Inspector.Target, TargetData());
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(a, a with { SchemaVersion = 2 }, false).Reason);
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(a, a with { Generation = 2 }, false).Reason);
        var sheet = Capture(Inspector.SheetRow, Sheet());
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(sheet, sheet with { ToolOptions = new(RowId: 2) }, false).Reason);
    }
    private static void IncompatibleProvenance()
    {
        var a = Capture(Inspector.SheetRow, Sheet());
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(a, a with { Payload = Sheet() with { Language = "Japanese" } }, false).Reason);
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(a, a with { Payload = Sheet() with { GameBuild = "2026.11.01.0000.0000" } }, false).Reason);
        var nodes = Capture(Inspector.AddonNodes, new NodesPayload("ContextMenu", 1, 1, [], NativeCompatibility.GameBuild));
        Equal(Reason.IncompatibleSnapshots, SnapshotComparer.Compare(nodes, nodes with { Payload = ((NodesPayload)nodes.Payload!) with { Lifetime = 2 } }, false).Reason);
    }
    private static void AmbiguousRows()
    {
        var a = Capture(Inspector.SheetRow, Sheet(F("Duplicate", "1"), F("Duplicate", "2")));
        var p = (ComparisonPayload)SnapshotComparer.Compare(a, a with { RequestId = Guid.NewGuid() }, false).Payload!; Equal(1, p.Ambiguous);
    }
    private static void ObjectIdentity()
    {
        using var e = new DiagnosticEngine(new FakeSource(1), new FakeClock()); e.Enqueue(Inspector.NearbyObjects); e.Tick();
        var a = e.View().Results[0] with { Objects = [new(ObjectCategory.Player, null, new(0, 0, 0), 0, true, Name: "Example")] };
        var b = a with { Objects = [], RequestId = Guid.NewGuid() };
        var p = (ComparisonPayload)SnapshotComparer.Compare(a, b, false).Payload!; Equal(0, p.Removed); Equal(1, p.Unobserved);
    }
    private static void TargetIdentity()
    {
        var a = Capture(Inspector.Target, TargetData()); var p = (ComparisonPayload)SnapshotComparer.Compare(a, a with { RequestId = Guid.NewGuid() }, false).Payload!;
        Check(!p.CompleteEvidence); Check(ReportCodec.Describe(a, 0).Contains("no claim of entity continuity"));
    }
    private static void CompareJob()
    {
        var source = new ToolSource { Read = (_, _, _, _) => new(Sheet(F("Name", "A"))) };
        using var e = new DiagnosticEngine(source, new FakeClock());
        e.EnqueueTool(Inspector.SheetRow, Options(Inspector.SheetRow)); e.Tick(); var a = e.View().Results[^1];
        e.EnqueueTool(Inspector.SheetRow, Options(Inspector.SheetRow)); e.Tick(); var b = e.View().Results[^1];
        var calls = source.ToolReads;
        e.EnqueueTool(Inspector.Compare, new(Baseline: a.RequestId, Comparison: b.RequestId)); e.Tick();
        Equal(calls, source.ToolReads); Check(e.View().Results[^1].Payload is ComparisonPayload);
    }
    private static void EvictedSources()
    {
        using var e = new DiagnosticEngine(new ToolSource(), new FakeClock());
        e.EnqueueTool(Inspector.Compare, new(Baseline: Guid.NewGuid(), Comparison: Guid.NewGuid())); e.Tick(); Equal(Reason.SnapshotUnavailable, e.View().Results[^1].Reason);
    }
    private static void CompareBounds()
    {
        var a = Capture(Inspector.Inventory, new InventoryPayload(Enumerable.Range(0, 256).Select(i => new InventoryRow(InventoryBag.Bag1, i, 1, "A", 1, false)).ToImmutableArray(), NativeCompatibility.GameBuild));
        var b = a with { RequestId = Guid.NewGuid(), Payload = new InventoryPayload(Enumerable.Range(256, 256).Select(i => new InventoryRow(InventoryBag.Bag1, i, 1, "B", 1, false)).ToImmutableArray(), NativeCompatibility.GameBuild) };
        var result = SnapshotComparer.Compare(a, b, false); Check(result.Partial); Check(((ComparisonPayload)result.Payload!).Rows.Length <= 256);
    }

    private sealed class ToolSource : IGameSource, IToolSource
    {
        public LiveContext Context = new(new(true, 100, true, new(0, 0, 0), 19, 100, new(false, false, false, false, false)), 123);
        public Func<Inspector, ToolOptions, int, long?, ToolStep> Read = (_, _, _, _) => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable);
        public int ToolReads, ContextReads;
        public LiveContext ReadContext() { ContextReads++; return Context; }
        public int ObjectTableLength => 1;
        public ObjectSample? ReadObject(int index) => throw new Exception("Phase 2 tools must not call nearby scanning");
        public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? stamp)
        { ToolReads++; return Read(inspector, options, cursor, stamp); }
    }
}
