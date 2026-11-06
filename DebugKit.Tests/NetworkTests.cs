using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DebugKit.Core;

internal static class NetworkTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("network requests require explicit consent and bounded fixed inputs", Inputs),
        ("network URI routes use only two HTTPS hosts and fixed enum scopes", Routes),
        ("market projection handles current missing median and mixed-world upload evidence", Market),
        ("market malformed scope item numeric depth and body data fail closed", Malformed),
        ("housing summary distinguishes reported plots from availability and handles empty", Housing),
        ("external missing and future timestamps remain unknown not fresh zero", MissingTimestamps),
        ("network HTTP is GET-only explicit and globally rate limited", HttpSuccess),
        ("network rejects redirects status errors and non-JSON without error payloads", HttpErrors),
        ("network enforces declared and streamed response byte limits", HttpSize),
        ("network 429 honors cooldown without retries", HttpRateLimit),
        ("network timeout and cancellation remain distinct", HttpCancellation),
        ("in-flight network cancellation reaches transport and disposed client rejects work", HttpInFlightCancellation),
        ("network engine starts once and waits without synchronous no-progress failure", EngineWaiting),
        ("network cancellation discards late completion", EngineCancellation),
        ("network lifecycle replacement and disposal discard late results", EngineLifecycle),
        ("network engine has independent monotonic timeout", EngineTimeout),
        ("provider and network payloads use generated JSON and refuse comparisons", Reports),
        ("network copied reports retain world DC and housing inputs on success and failure", CapturedInputReports),
        ("network HTTP status and frozen inputs survive JSON text and exact export without error bodies", HttpStatusReports),
        ("network responses without headers never fabricate HTTP status", MissingHttpStatus),
        ("engine bounds HTTP status and rejects status on non-network tools", HttpStatusBounds),
    ];
    private static void Check(bool value) { if (!value) throw new Exception("Network assertion failed"); }
    private static ToolOptions Options => new(ItemId: 5333, WorldId: 74, NetworkConsent: true);
    private const string MarketJson = """
        {"results":[{"itemId":5333,
        "nq":{"minListing":{"world":{"price":1},"dc":{"price":2}},"averageSalePrice":{"world":{"price":4.8},"dc":{"price":5}},"dailySaleVelocity":{"world":{"quantity":1.3},"dc":{"quantity":8}}},
        "hq":{"minListing":{"world":{"price":9},"dc":{"price":3}},"averageSalePrice":{"world":{"price":3},"dc":{"price":4}},"dailySaleVelocity":{"world":{"quantity":0.5},"dc":{"quantity":1}}},
        "worldUploadTimes":[{"worldId":74,"timestamp":1793731914347},{"worldId":34,"timestamp":1793787733454}],
        "sellerID":"PRIVATE-IDENTITY","buyerName":"PRIVATE-NAME"}],"failedItems":[]}
        """;
    private const string HousingJson = """
        {"id":339,"name":"Mist","num_open_plots":1,"oldest_plot_time":1793719544,
        "open_plots":[{"world_id":74,"district_id":339,"ward_number":12,"plot_number":55,"last_updated_time":1793832422.3,"lotto_phase":3,"lotto_entries":0}]}
        """;
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static ToolStep Decode(string value, Inspector inspector = Inspector.MarketSummary, ToolOptions? options = null) =>
        NetworkDecoder.Decode(inspector, options ?? Options, Bytes(value), new FakeClock().UtcNow);
    private static void Inputs()
    {
        Check(Options.IsValid(Inspector.MarketSummary));
        Check(!(Options with { NetworkConsent = false }).IsValid(Inspector.MarketSummary));
        Check(!(Options with { WorldId = 0 }).IsValid(Inspector.MarketSummary));
        Check(!(Options with { ItemId = 0 }).IsValid(Inspector.MarketSummary));
        Check(!(Options with { District = (HousingDistrict)1 }).IsValid(Inspector.HousingService));
        Check(!(Options with { MarketScope = (MarketScope)99 }).IsValid(Inspector.MarketSummary));
        var dc = Options with { MarketScope = MarketScope.DataCenter, WorldId = 0 };
        Check(dc.IsValid(Inspector.MarketSummary));
        Check(dc.Canonical(Inspector.MarketSummary).WorldId == 74);
        Check((Options with { ExperimentalNative = true, Provider = ProviderKind.WahTools }).Canonical(Inspector.MarketSummary) == Options);
    }
    private static void Routes()
    {
        Check(NetworkPolicy.RequestUri(Inspector.MarketSummary, Options).AbsoluteUri == "https://universalis.app/api/v2/aggregated/74/5333");
        Check(NetworkPolicy.RequestUri(Inspector.MarketSummary, Options with { MarketScope = MarketScope.DataCenter, DataCenter = MarketDataCenter.Light }).AbsoluteUri == "https://universalis.app/api/v2/aggregated/Light/5333");
        Check(NetworkPolicy.RequestUri(Inspector.HousingService, Options).AbsoluteUri == "https://paissadb.zhu.codes/worlds/74/339");
        Check(NetworkPolicy.RequestUri(Inspector.HousingService, Options).Query == "");
    }
    private static void Market()
    {
        var step = Decode(MarketJson);
        Check(step.Outcome == Outcome.Success && step.Payload is NetworkPayload { Service: NetworkService.Universalis, Rows.Length: 3 });
        var payload = (NetworkPayload)step.Payload!;
        Check(payload.Rows[0].Fields.First(x => x.Field == "MedianListingPrice").Value == "unavailable");
        Check(payload.Rows[2].Fields.First(x => x.Field == "WorldUploadCount").Value == "1");
        var dc = Decode(MarketJson, options: Options with { MarketScope = MarketScope.DataCenter });
        Check(((NetworkPayload)dc.Payload!).Rows[0].Fields[0].Value == "2");
        Check(((NetworkPayload)dc.Payload!).Rows[2].Name.Contains("outside requested DC"));
        Check(!string.Join(";", payload.Rows.SelectMany(x => x.Fields).Select(x => x.Value)).Contains("PRIVATE"));
    }
    private static void Malformed()
    {
        foreach (var json in new[] { "[]", "{", MarketJson.Replace("5333", "1"), MarketJson.Replace("\"price\":1}", "\"price\":-1}"), MarketJson.Replace("\"price\":9", "\"price\":-9") })
            Check(Decode(json).Reason == Reason.NetworkSchemaMismatch);
        Check(NetworkDecoder.Decode(Inspector.MarketSummary, Options, new byte[NetworkPolicy.MaximumResponseBytes + 1], DateTimeOffset.UtcNow).Reason == Reason.ResponseTooLarge);
        Check(Decode(new string('[', 20) + "0" + new string(']', 20)).Reason == Reason.NetworkSchemaMismatch);
    }
    private static void Housing()
    {
        var good = Decode(HousingJson, Inspector.HousingService);
        Check(good.Payload is NetworkPayload { Service: NetworkService.PaissaDb, Rows.Length: 1 });
        var p = (NetworkPayload)good.Payload!;
        Check(p.Rows[0].Fields[0].Field.Contains("not purchase availability"));
        Check(Decode(HousingJson.Replace("\"world_id\":74", "\"world_id\":1"), Inspector.HousingService).Reason == Reason.NetworkSchemaMismatch);
        var empty = Decode("{\"id\":339,\"num_open_plots\":0,\"open_plots\":[]}", Inspector.HousingService);
        Check(empty.Payload is NetworkPayload { Rows.Length: 1 } e && e.Rows[0].Fields[0].Value == "0" && e.Rows[0].Fields[2].Value == "unavailable");
    }
    private static void MissingTimestamps()
    {
        var missing = (NetworkPayload)Decode(MarketJson.Replace("worldUploadTimes", "missingUploadTimes")).Payload!;
        Check(missing.Rows[2].Fields.All(x => x.Value == "unavailable"));
        var epoch = new FakeClock().UtcNow.AddDays(1).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var future = (NetworkPayload)Decode(MarketJson.Replace("1793731914347", epoch)).Payload!;
        Check(future.Rows[2].Fields.First(x => x.Field == "NewestUploadAgeSeconds").Value == "unavailable");
        var housing = (NetworkPayload)Decode(HousingJson.Replace("\"last_updated_time\":1793832422.3", "\"last_updated_time\":null"), Inspector.HousingService).Payload!;
        Check(housing.Rows[0].Fields.First(x => x.Field == "MissingObservationTimestamps").Value == "1");
        Check(housing.Rows[0].Fields.First(x => x.Field == "OldestObservationAgeSeconds").Value == "unavailable");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return send(request, cancellationToken); }
    }
    private static HttpResponseMessage Response(string body = MarketJson, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static ToolStep Capture(NetworkCaptureClient client, CancellationToken token = default, ToolOptions? options = null) =>
        client.CaptureAsync(Inspector.MarketSummary, options ?? Options, token).GetAwaiter().GetResult();
    private static void HttpSuccess()
    {
        var clock = new FakeClock();
        var handler = new Handler((request, _) =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "universalis.app" && request.Content == null);
            Check(request.Headers.Authorization == null);
            return Task.FromResult(Response());
        });
        using var client = new NetworkCaptureClient(clock, handler);
        Check(Capture(client, options: Options with { NetworkConsent = false }).Reason == Reason.NetworkConsentRequired && handler.Calls == 0);
        Check(Capture(client) is { Outcome: Outcome.Success, HttpStatusCode: 200 } && handler.Calls == 1);
        Check(Capture(client) is { Reason: Reason.NetworkRateLimited, HttpStatusCode: null } && handler.Calls == 1);
        clock.Milliseconds += 10000;
        Check(Capture(client).Outcome == Outcome.Success && handler.Calls == 2);
    }
    private static void HttpErrors()
    {
        foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
        {
            var handler = new Handler((_, _) => Task.FromResult(Response("PRIVATE ERROR", status)));
            using var client = new NetworkCaptureClient(testHandler: handler);
            var result = Capture(client);
            Check(result.Reason == Reason.NetworkHttpError && result.HttpStatusCode == (int)status && handler.Calls == 1);
        }
        using var html = new NetworkCaptureClient(testHandler: new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("PRIVATE HTML") })));
        Check(Capture(html) is { Reason: Reason.NetworkSchemaMismatch, HttpStatusCode: 200 });
    }
    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
    private static void HttpSize()
    {
        foreach (var chunked in new[] { false, true })
        {
            var handler = new Handler((_, _) =>
            {
                HttpContent content = chunked ? new ChunkedContent(new byte[NetworkPolicy.MaximumResponseBytes + 1]) : new ByteArrayContent(new byte[1]);
                content.Headers.ContentType = new("application/json");
                if (!chunked) content.Headers.ContentLength = NetworkPolicy.MaximumResponseBytes + 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            });
            using var client = new NetworkCaptureClient(testHandler: handler);
            Check(Capture(client).Reason == Reason.ResponseTooLarge);
        }
    }
    private static void HttpRateLimit()
    {
        var clock = new FakeClock();
        var handler = new Handler((_, _) =>
        {
            var response = Response("PRIVATE", (HttpStatusCode)429);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        });
        using var client = new NetworkCaptureClient(clock, handler);
        Check(Capture(client) is { Reason: Reason.NetworkRateLimited, HttpStatusCode: 429 });
        clock.Milliseconds += 60000;
        Check(Capture(client).Reason == Reason.NetworkRateLimited && handler.Calls == 1);
        clock.Milliseconds += 60000;
        Check(Capture(client).Reason == Reason.NetworkRateLimited && handler.Calls == 2);
    }
    private static void HttpCancellation()
    {
        var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(); });
        using var client = new NetworkCaptureClient(testHandler: handler, timeout: TimeSpan.FromMilliseconds(5));
        Check(Capture(client).Reason == Reason.NetworkTimeout);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var other = new NetworkCaptureClient(testHandler: new Handler((_, _) => throw new Exception("PRIVATE ERROR")));
        Check(Capture(other, cancelled.Token).Reason == Reason.UserCancelled);
        Check(Capture(other).Reason == Reason.NetworkFailed);
    }
    private static void HttpInFlightCancellation()
    {
        var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(); });
        using var client = new NetworkCaptureClient(testHandler: handler);
        using var cancellation = new CancellationTokenSource();
        var task = client.CaptureAsync(Inspector.MarketSummary, Options, cancellation.Token);
        Check(handler.Calls == 1 && !task.IsCompleted);
        cancellation.Cancel();
        Check(task.GetAwaiter().GetResult().Reason == Reason.UserCancelled && handler.Calls == 1);
        client.Dispose(); Check(Capture(client).Reason == Reason.Disposed && handler.Calls == 1);
    }
    private sealed class Source : IGameSource, IToolSource, IAsyncToolSource
    {
        public LiveContext Context = new(new(false, null, false, null, null, null, new(false, false, false, false, false)), 0);
        public TaskCompletionSource<ToolStep> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public int Calls;
        public ToolStep? Synchronous;
        public LiveContext ReadContext() => Context;
        public int ObjectTableLength => 0;
        public ObjectSample? ReadObject(int _) => null;
        public ToolStep ReadToolStep(Inspector inspector, ToolOptions options, int cursor, long? expectedStamp) => Synchronous ?? throw new Exception("Not synchronous");
        public Task<ToolStep> StartNetworkCapture(Inspector inspector, ToolOptions options, CancellationToken cancellationToken)
        { Calls++; Token = cancellationToken; return Completion.Task; }
    }
    private static void EngineWaiting()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        Check(engine.EnqueueTool(Inspector.MarketSummary, Options) == QueueStatus.Accepted);
        for (var i = 0; i < 200; i++) engine.Tick();
        Check(source.Calls == 1 && engine.View().Results.Length == 0 && engine.View().ActiveRequest != null);
        source.Completion.SetResult(Decode(MarketJson)); engine.Tick();
        Check(engine.View().Results[^1] is { Outcome: Outcome.Success, Steps: 1, NonAtomic: false, Payload: NetworkPayload });
    }
    private static void EngineCancellation()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.MarketSummary, Options); engine.Tick(); engine.Cancel();
        Check(source.Token.IsCancellationRequested && engine.View().Results[^1].Reason == Reason.UserCancelled);
        source.Completion.SetResult(Decode(MarketJson) with { HttpStatusCode = 200 }); engine.Tick();
        Check(engine.View().Results.Length == 1 && engine.View().Results[0].Payload == null && engine.View().Results[0].HttpStatusCode == null);
    }
    private static void EngineLifecycle()
    {
        foreach (var dispose in new[] { false, true })
        {
            var source = new Source();
            using var engine = new DiagnosticEngine(source, new FakeClock());
            engine.EnqueueTool(Inspector.MarketSummary, Options); engine.Tick();
            if (dispose) engine.Dispose();
            else { source.Context = source.Context with { CharacterToken = 1, Data = source.Context.Data with { LoggedIn = true } }; engine.Tick(); }
            Check(source.Token.IsCancellationRequested);
            source.Completion.SetResult(Decode(MarketJson)); engine.Tick();
            Check(engine.View().Results.All(r => r.Outcome == Outcome.Cancelled && r.Payload == null));
        }
    }
    private static void EngineTimeout()
    {
        var clock = new FakeClock(); var source = new Source();
        using var engine = new DiagnosticEngine(source, clock);
        engine.EnqueueTool(Inspector.MarketSummary, Options); engine.Tick(); clock.Milliseconds += 15001; engine.Tick();
        Check(engine.View().Results[^1].Reason == Reason.NetworkTimeout && source.Token.IsCancellationRequested);
        source.Completion.SetResult(Decode(MarketJson)); engine.Tick(); Check(engine.View().Results.Length == 1);
    }
    private static void Reports()
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.MarketSummary, Options); engine.Tick(); source.Completion.SetResult(Decode(MarketJson)); engine.Tick();
        var first = engine.View().Results[^1];
        Check(SnapshotComparer.Compare(first, first with { RequestId = Guid.NewGuid() }, false).Reason == Reason.IncompatibleSnapshots);
        var metadata = ProviderCatalog.Metadata([new("AutoRetainer", "4.6.1.26", true, false)], true);
        var provider = first with { Inspector = Inspector.ProviderMetadata, Payload = metadata };
        var query = first with { Inspector = Inspector.ProviderReadiness, Payload = new ProviderQueryPayload(metadata.Rows[0], ProviderQuery.Suppressed, false, "AutoRetainer.GetSuppressed") };
        foreach (var result in new[] { first, provider, query })
        {
            var view = engine.View() with { Results = [result] };
            var json = ReportCodec.Create(view, Provenance.Create("0.4.0", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
            using var doc = JsonDocument.Parse(json.Content);
            Check(doc.RootElement.GetProperty("Results")[0].GetProperty("Payload").GetProperty("$type").GetString() is "network" or "provider" or "providerQuery");
            Check(!json.Content.Contains("PRIVATE") && ReportCodec.Describe(result, 0).Length > 0);
            Check(SnapshotComparer.Compare(result, result with { RequestId = Guid.NewGuid() }, false).Reason == Reason.IncompatibleSnapshots);
        }
    }
    private static CaptureResult Retain(Inspector inspector, ToolOptions options, ToolStep response)
    {
        var source = new Source();
        using var engine = new DiagnosticEngine(source, new FakeClock());
        Check(engine.EnqueueTool(inspector, options) == QueueStatus.Accepted);
        engine.Tick(); source.Completion.SetResult(response); engine.Tick();
        return engine.View().Results[^1];
    }
    private static EngineView View(CaptureResult result) => new(0, 0, null, 0, [result], 0, 0, 0, true);
    private static void CapturedInputReports()
    {
        foreach (var (inspector, options, expected) in new[]
        {
            (Inspector.MarketSummary, Options, "item 5333 · world 74"),
            (Inspector.MarketSummary, Options with { MarketScope = MarketScope.DataCenter, DataCenter = MarketDataCenter.Light }, "item 5333 · DC Light"),
            (Inspector.HousingService, Options with { WorldId = 81, District = HousingDistrict.Empyreum }, "world 81 · Empyreum"),
        })
        foreach (var success in new[] { false, true })
        {
            var body = inspector == Inspector.HousingService ? HousingJson.Replace("\"id\":339", "\"id\":979")
                .Replace("\"district_id\":339", "\"district_id\":979").Replace("\"world_id\":74", "\"world_id\":81") : MarketJson;
            var response = success ? Decode(body, inspector, options) with { HttpStatusCode = 200 }
                : new ToolStep(null, Outcome: Outcome.Unavailable, Reason: Reason.NetworkHttpError, HttpStatusCode: 404);
            var result = Retain(inspector, options, response);
            Check(result.Outcome == (success ? Outcome.Success : Outcome.Unavailable));
            var text = ReportCodec.Describe(result, 0);
            Check(text.Contains("Captured inputs: " + expected) && text.Contains(success ? "HTTP status: 200" : "HTTP status: 404"));
            if (options.MarketScope == MarketScope.DataCenter) Check(!text.Contains("world 74"));
            Check(result.SchemaVersion == 2);
        }
    }
    private static void HttpStatusReports()
    {
        using var client = new NetworkCaptureClient(testHandler: new Handler((_, _) => Task.FromResult(Response("PRIVATE ERROR BODY", HttpStatusCode.NotFound))));
        var result = Retain(Inspector.MarketSummary, Options, Capture(client));
        Check(result.HttpStatusCode == 404 && result.Payload == null);
        var directory = Path.Combine(Path.GetTempPath(), "debugkit-network-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var exporter = new ReportExporter(directory);
            foreach (var format in new[] { ReportFormat.Json, ReportFormat.Text })
            {
                var preview = ReportCodec.Create(View(result), Provenance.Create("0.4.2", "15.0.3.9"), format, DateTimeOffset.UtcNow);
                Check(!preview.Content.Contains("PRIVATE"));
                if (format == ReportFormat.Json)
                {
                    using var json = JsonDocument.Parse(preview.Content);
                    var record = json.RootElement.GetProperty("Results")[0];
                    Check(record.GetProperty("HttpStatusCode").GetInt32() == 404 && record.GetProperty("ToolOptions").GetProperty("ItemId").GetUInt32() == 5333);
                }
                else Check(preview.Content.Contains("HTTP status: 404") && preview.Content.Contains("item 5333 · world 74"));
                Check(exporter.ExportAsync(preview).GetAwaiter().GetResult() == ExportStatus.Success);
                var path = Path.Combine(directory, preview.FileName);
                Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(preview.Bytes.AsSpan()));
                File.Delete(path);
            }
        }
        finally { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
    }
    private static void MissingHttpStatus()
    {
        foreach (var reason in new[] { Reason.NetworkTimeout, Reason.NetworkFailed, Reason.NetworkRateLimited })
        {
            var result = Retain(Inspector.MarketSummary, Options, new(null, Outcome: Outcome.Unavailable, Reason: reason));
            Check(result.HttpStatusCode == null && ReportCodec.Describe(result, 0).Contains("HTTP status: unavailable"));
            var preview = ReportCodec.Create(View(result), Provenance.Create("0.4.2", "15.0.3.9"), ReportFormat.Json, DateTimeOffset.UtcNow);
            using var json = JsonDocument.Parse(preview.Content);
            Check(!json.RootElement.GetProperty("Results")[0].TryGetProperty("HttpStatusCode", out _));
        }
    }
    private static void HttpStatusBounds()
    {
        foreach (var status in new[] { -1, 0, 99, 100, 200, 404, 429, 599, 600, int.MaxValue })
        {
            var result = Retain(Inspector.MarketSummary, Options, new(null, Outcome: Outcome.Unavailable, Reason: Reason.NetworkHttpError, HttpStatusCode: status));
            Check(result.HttpStatusCode == (status is >= 100 and <= 599 ? (int?)status : null));
        }
        var source = new Source { Synchronous = new(ProviderCatalog.Metadata([], true), HttpStatusCode: 404) };
        using var engine = new DiagnosticEngine(source, new FakeClock());
        engine.EnqueueTool(Inspector.ProviderMetadata, new()); engine.Tick();
        Check(engine.View().Results[^1].HttpStatusCode == null);
    }
}
