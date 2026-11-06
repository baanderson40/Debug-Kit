using System.Collections.Immutable;

namespace DebugKit.Core;

/// <summary>Bounded, serialized coordinator. UI calls enqueue/view only; Tick is framework-thread-only.</summary>
public sealed class DiagnosticEngine : IDisposable
{
    private sealed record Request(Guid Id, Inspector Inspector, CaptureOptions? Options, long Generation, DateTimeOffset Created, ToolOptions? ToolOptions = null);
    private sealed class Work(Request request, DateTimeOffset started, long timestamp)
    {
        public Request Request { get; } = request;
        public DateTimeOffset Started { get; } = started;
        public long Timestamp { get; } = timestamp;
        public ContextData? Context;
        public Point Origin;
        public int Length, Index, Steps, Examined, Matched, Empty, Invalid, Filtered, Unavailable;
        public readonly List<ObjectRow> Rows = new(128);
        public ToolPayload? Payload;
        public int Cursor;
        public long? SourceStamp;
        public bool Partial;
        public Reason UnavailableReason;
        public int Omitted;
        public int? HttpStatusCode;
        public Task<ToolStep>? AsyncTask;
        public CancellationTokenSource? AsyncCancellation;
        public bool CallbackStopRequested;
        public bool ObservationStarted, ObservationStopRequested;
    }

    private readonly object gate = new();
    private readonly IGameSource source;
    private readonly IClock clock;
    private readonly Limits limits;
    private readonly IToolSource? tools;
    private readonly Queue<Request> pending = new();
    private readonly List<(CaptureResult Result, int Bytes)> history = new();
    private Work? active;
    private LiveContext? lastContext;
    private bool sourceFailed, disposed, commandAvailable;
    private long generation, evicted, dropped, rejected;
    private int bytes;
    public PerformanceRecorder Performance { get; } = new();

    public DiagnosticEngine(IGameSource source, IClock? clock = null, Limits? limits = null, IToolSource? tools = null)
    {
        this.source = source;
        this.clock = clock ?? new SystemClock();
        this.limits = limits ?? new Limits();
        this.limits.Validate();
        this.tools = tools ?? source as IToolSource;
    }

    public void SetCommandAvailable(bool value) { lock (gate) commandAvailable = value; }

    public QueueStatus Enqueue(Inspector inspector, CaptureOptions? options = null)
    {
        lock (gate)
        {
            if (disposed) return QueueStatus.Disposed;
            if (Enum.IsDefined(inspector) && inspector is not (Inspector.Context or Inspector.NearbyObjects))
                return EnqueueTool(inspector, new());
            if (!Enum.IsDefined(inspector) || (inspector == Inspector.NearbyObjects && !(options ?? new()).IsValid))
            {
                rejected++;
                return QueueStatus.Invalid;
            }
            options = inspector == Inspector.Context ? null : options ?? new();
            bool Same(Request r) => r.Inspector == inspector && r.Options == options && r.Generation == generation;
            if ((active != null && Same(active.Request)) || pending.Any(Same)) { rejected++; return QueueStatus.Duplicate; }
            if (pending.Count >= limits.Queue) { rejected++; return QueueStatus.Full; }
            pending.Enqueue(new(Guid.NewGuid(), inspector, options, generation, clock.UtcNow));
            return QueueStatus.Accepted;
        }
    }

    public QueueStatus EnqueueTool(Inspector inspector, ToolOptions options)
    {
        lock (gate)
        {
            if (disposed) return QueueStatus.Disposed;
            if (!Enum.IsDefined(inspector) || !options.IsValid(inspector)) { rejected++; return QueueStatus.Invalid; }
            options = options.Canonical(inspector);
            bool Same(Request r) => r.Inspector == inspector && r.ToolOptions == options && r.Generation == generation;
            if ((active != null && Same(active.Request)) || pending.Any(Same)) { rejected++; return QueueStatus.Duplicate; }
            if (pending.Count >= limits.Queue) { rejected++; return QueueStatus.Full; }
            pending.Enqueue(new(Guid.NewGuid(), inspector, null, generation, clock.UtcNow, options));
            return QueueStatus.Accepted;
        }
    }

    public EngineView View()
    {
        lock (gate)
            return new(generation, pending.Count, active?.Request.Id, active?.Examined ?? 0,
                history.Select(x => x.Result).ToImmutableArray(), evicted, dropped, rejected, commandAvailable);
    }

    public bool HasCallbackCapture
    {
        get { lock (gate) return active?.Request.Inspector == Inspector.AddonCallbacks; }
    }

    public bool CanStartCallbackCapture
    {
        get { lock (gate) return !disposed && active == null && pending.Count == 0; }
    }

    public bool HasObservation
    {
        get { lock (gate) return active != null && IsObservation(active.Request.Inspector); }
    }

    public Inspector? ActiveObservation
    {
        get { lock (gate) return active != null && IsObservation(active.Request.Inspector) ? active.Request.Inspector : null; }
    }

    public bool CanStartObservation
    {
        get { lock (gate) return !disposed && active == null && pending.Count == 0; }
    }

    public bool StopObservation()
    {
        lock (gate)
        {
            if (active == null || !IsObservation(active.Request.Inspector)) return false;
            active.ObservationStopRequested = true;
            return true;
        }
    }

    public bool StopCallbackCapture()
    {
        lock (gate)
        {
            if (active?.Request.Inspector != Inspector.AddonCallbacks) return false;
            active.CallbackStopRequested = true;
            (tools as ICallbackToolSource)?.RequestCallbackStop();
            return true;
        }
    }

    public void ClearResults()
    {
        lock (gate) { history.Clear(); bytes = 0; }
    }

    public void Cancel() { lock (gate) CancelWork(Reason.UserCancelled); }

    // Events can invalidate even if a transition starts and finishes between sampled updates.
    public void Invalidate()
    {
        lock (gate)
        {
            if (disposed) return;
            generation++;
            lastContext = null;
            CancelWork(Reason.ContextChanged);
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            using var measurement = Performance.Measure(active?.Request.Inspector ?? pending.FirstOrDefault()?.Inspector,
                PerformanceArea.FrameworkStep);
            TickCore();
        }
    }

    private void TickCore()
    {
        lock (gate)
        {
            if (disposed) return;
            LiveContext current;
            try
            {
                using (Performance.Measure(active?.Request.Inspector ?? pending.FirstOrDefault()?.Inspector, PerformanceArea.ContextRead))
                    current = Normalize(source.ReadContext());
                if (sourceFailed || (lastContext is { } old && !SameContext(old, current)))
                {
                    generation++;
                    CancelWork(Reason.ContextChanged);
                }
                sourceFailed = false;
                lastContext = current;
            }
            catch (Exception)
            {
                if (!sourceFailed) generation++;
                sourceFailed = true;
                lastContext = null;
                FailAll();
                return;
            }

            if (active == null && pending.TryDequeue(out var request))
                active = new(request, clock.UtcNow, clock.Timestamp);
            if (active is not { } work) return;
            if (work.Request.Generation != generation) { Finish(Outcome.Cancelled, Reason.ContextChanged, true); return; }
            work.Steps++;
            work.Context ??= current.Data;

            if (work.Request.ToolOptions != null)
            {
                StepTool(work, current);
                return;
            }

            if (work.Request.Inspector == Inspector.Context)
            {
                Finish(Outcome.Success, Reason.None, false);
                return;
            }

            if (!current.Data.LoggedIn) { Finish(Outcome.Blocked, Reason.LoggedOut, false); return; }
            if (current.Data.Conditions.BetweenAreas) { Finish(Outcome.Blocked, Reason.Loading, false); return; }
            if (!current.Data.PlayerAvailable || current.Data.Position is not { } position)
            { Finish(Outcome.Unavailable, Reason.PlayerUnavailable, false); return; }

            try
            {
                if (work.Steps == 1)
                {
                    work.Origin = position;
                    work.Length = source.ObjectTableLength;
                    if (work.Length <= 0)
                    {
                        Finish(Outcome.Unavailable, Reason.SourceUnavailable, false);
                        return;
                    }
                }
                var start = clock.Timestamp;
                for (var count = 0; count < limits.EntriesPerStep && work.Index < work.Length && work.Examined < limits.Entries; count++)
                {
                    if (count > 0 && clock.ElapsedMilliseconds(start) >= limits.StepMilliseconds) break;
                    work.Examined++;
                    ObjectSample? sample;
                    try
                    {
                        using var measurement = Performance.Measure(work.Request.Inspector, PerformanceArea.ReaderJob);
                        sample = source.ReadObject(work.Index++);
                    }
                    catch (Exception) { work.Unavailable++; continue; }
                    if (sample is null) { work.Empty++; continue; }
                    var obj = sample.Value;
                    if (!obj.Valid || !obj.Position.IsFinite || !Enum.IsDefined(obj.Kind) || obj.Kind == ObjectCategory.Any)
                    { work.Invalid++; continue; }
                    var options = work.Request.Options!;
                    var distance = obj.Position.Distance(work.Origin);
                    if (distance > options.Radius ||
                        (options.Kind != ObjectCategory.Any && options.Kind != obj.Kind) ||
                        (options.BaseId is { } id && id != obj.BaseId)) { work.Filtered++; continue; }
                    work.Matched++;
                    // Player and unknown-category base IDs are intentionally absent from retained rows.
                    var baseId = obj.Kind is ObjectCategory.Player or ObjectCategory.Other ? null : obj.BaseId;
                    var rawLabel = Enum.IsDefined(obj.RawKindLabel) ? obj.RawKindLabel : NativeKindLabel.Unknown;
                    work.Rows.Add(new(obj.Kind, baseId, obj.Position, distance, obj.Targetable, obj.RawKindValue, rawLabel,
                        ObjectNames.For(obj.Kind, obj.Name)));
                    if (work.Rows.Count >= limits.Rows) break;
                }
                // Recheck after reads, too: a fake/provider can invalidate context during a single step.
                LiveContext after;
                using (Performance.Measure(work.Request.Inspector, PerformanceArea.ContextRead))
                    after = Normalize(source.ReadContext());
                if (!SameContext(current, after))
                {
                    generation++;
                    lastContext = after;
                    CancelWork(Reason.ContextChanged);
                    return;
                }
                if (work.Index >= work.Length)
                    Finish(work.Unavailable > 0 ? Outcome.Unavailable : Outcome.Success,
                        work.Unavailable > 0 ? Reason.ObjectReadFailed : Reason.None, work.Unavailable > 0);
                else if (work.Rows.Count >= limits.Rows) Finish(Outcome.Success, Reason.RowLimit, true);
                else if (work.Examined >= limits.Entries) Finish(Outcome.Success, Reason.EntryLimit, true);
            }
            catch (Exception) { Finish(Outcome.Failed, Reason.SourceUnavailable, true); }
        }
    }

    private void StepTool(Work work, LiveContext current)
    {
        try
        {
            if (IsObservation(work.Request.Inspector))
            {
                if (tools is not IObservationToolSource observer) { Finish(Outcome.Unavailable, Reason.SourceUnavailable, false); return; }
                if (work.ObservationStopRequested)
                {
                    Finish(work.Payload == null ? Outcome.Unavailable : Outcome.Success,
                        work.Payload == null ? Reason.SourceUnavailable : Reason.None,
                        work.Payload == null || work.Payload is ObservationPayload { Dropped: > 0 });
                    return;
                }
                work.ObservationStarted = true;
                var sample = observer.SampleObservation(work.Request.Inspector, work.Request.ToolOptions!);
                if (sample.Payload is not null)
                {
                    if (sample.Payload is not ObservationPayload observation) { Finish(Outcome.Failed, Reason.SourceUnavailable, true); return; }
                    work.Payload = Payloads.Merge(work.Payload, Payloads.Sanitize(observation));
                    work.Examined += sample.Examined; work.Matched += sample.Matched;
                    work.Unavailable += sample.Unavailable; work.Partial |= sample.Partial;
                }
                LiveContext observationContext;
                using (Performance.Measure(work.Request.Inspector, PerformanceArea.ContextRead)) observationContext = Normalize(source.ReadContext());
                if (!SameContext(current, observationContext)) { generation++; lastContext = observationContext; CancelWork(Reason.ContextChanged); }
                return;
            }
            ToolStep step;
            if (NetworkPolicy.IsNetwork(work.Request.Inspector) || work.Request.Inspector == Inspector.AddonCallbacks)
            {
                if (work.AsyncTask == null)
                {
                    work.AsyncCancellation = new();
                    if (work.Request.Inspector == Inspector.AddonCallbacks && tools is ICallbackToolSource callbackSource)
                    {
                        work.AsyncTask = callbackSource.StartCallbackCapture(work.Request.ToolOptions!, work.AsyncCancellation.Token);
                        if (work.CallbackStopRequested) callbackSource.RequestCallbackStop();
                    }
                    else if (NetworkPolicy.IsNetwork(work.Request.Inspector) && tools is IAsyncToolSource asyncSource)
                        work.AsyncTask = asyncSource.StartNetworkCapture(work.Request.Inspector, work.Request.ToolOptions!, work.AsyncCancellation.Token);
                    else { Finish(Outcome.Unavailable, Reason.SourceUnavailable, false); return; }
                }
                if (NetworkPolicy.IsNetwork(work.Request.Inspector) && clock.ElapsedMilliseconds(work.Timestamp) > 15000)
                { Finish(Outcome.Unavailable, Reason.NetworkTimeout, true); return; }
                if (!work.AsyncTask.IsCompleted)
                {
                    work.Steps--;
                    return;
                }
                step = work.AsyncTask.GetAwaiter().GetResult(); // Completed only; never blocks framework thread.
            }
            else
            {
                using (Performance.Measure(work.Request.Inspector, PerformanceArea.ReaderJob))
                    step = ToolJobs.Step(work.Request.Inspector, work.Request.ToolOptions!, work.Cursor, work.SourceStamp,
                        tools, id => history.Select(x => x.Result).FirstOrDefault(r => r.RequestId == id));
            }
            if (NetworkPolicy.IsNetwork(work.Request.Inspector))
                work.HttpStatusCode = step.HttpStatusCode is >= 100 and <= 599 ? step.HttpStatusCode : null;
            if (work.SourceStamp is { } stamp && step.SourceStamp is { } next && stamp != next)
            { Finish(Outcome.Cancelled, Reason.SourceChanged, true); return; }
            work.SourceStamp ??= step.SourceStamp;
            var sanitized = Payloads.Sanitize(step.Payload);
            if ((step.Payload is DomainPayload domain && domain.Kind != work.Request.Inspector) ||
                (work.Payload is DomainPayload oldDomain && sanitized is DomainPayload nextDomain && !DomainPolicies.SameEvidence(oldDomain, nextDomain)) ||
                (NetworkPolicy.IsNetwork(work.Request.Inspector) && step.Payload != null &&
                    (step.Payload is not NetworkPayload external || external.Service !=
                        (work.Request.Inspector == Inspector.MarketSummary ? NetworkService.Universalis : NetworkService.PaissaDb))) ||
                (work.Request.Inspector == Inspector.AddonCallbacks && step.Payload != null && step.Payload is not CallbackPayload) ||
                (step.Payload is CallbackPayload && work.Request.Inspector != Inspector.AddonCallbacks))
            { Finish(Outcome.Cancelled, Reason.SourceChanged, true); return; }
            work.Examined += step.Examined; work.Matched += step.Matched; work.Empty += step.Empty;
            work.Invalid += step.Invalid; work.Filtered += step.Filtered; work.Unavailable += step.Unavailable;
            work.Partial |= step.Partial;
            if (step.Unavailable > 0) work.UnavailableReason = step.Reason;
            var omitted = Math.Max(0, Payloads.Count(step.Payload) - Payloads.Count(sanitized));
            work.Omitted += omitted; work.Partial |= omitted > 0;
            work.Payload = Payloads.Merge(work.Payload, sanitized);
            LiveContext after;
            using (Performance.Measure(work.Request.Inspector, PerformanceArea.ContextRead))
                after = Normalize(source.ReadContext());
            if (!SameContext(current, after))
            {
                generation++; lastContext = after; CancelWork(Reason.ContextChanged); return;
            }
            var rowLimit = DomainPolicies.IsDomain(work.Request.Inspector) && work.Request.Inspector is not (Inspector.WorldPlacements or Inspector.CollisionBounds) ? 128 : 256;
            if (Payloads.Count(work.Payload) > rowLimit)
            {
                while (Payloads.Count(work.Payload) > rowLimit) { work.Payload = Payloads.TrimLast(work.Payload); work.Omitted++; }
                Finish(work.Unavailable > 0 ? Outcome.Unavailable : step.Outcome,
                    work.Unavailable > 0 ? work.UnavailableReason : step.Outcome == Outcome.Success ? Reason.RowLimit : step.Reason, true); return;
            }
            if (step.Complete)
            {
                if (step.Outcome == Outcome.Success && work.Payload == null)
                { Finish(Outcome.Unavailable, Reason.SourceUnavailable, true); return; }
                var unavailable = work.Unavailable > 0 && step.Outcome == Outcome.Success;
                Finish(unavailable ? Outcome.Unavailable : step.Outcome,
                    unavailable ? work.UnavailableReason : step.Reason, work.Partial || unavailable);
                return;
            }
            if (step.NextCursor <= work.Cursor || work.Examined >= limits.Entries || work.Steps >= 128)
            { Finish(work.Unavailable > 0 ? Outcome.Unavailable : Outcome.Success,
                work.Unavailable > 0 ? work.UnavailableReason : Reason.EntryLimit, true); return; }
            work.Cursor = step.NextCursor;
        }
        catch (Exception) { Finish(Outcome.Failed, Reason.SourceUnavailable, true); }
    }

    private static LiveContext Normalize(LiveContext context)
    {
        var data = context.Data;
        var available = data.LoggedIn && data.PlayerAvailable && !data.Conditions.BetweenAreas;
        data = data with
        {
            TerritoryId = data.LoggedIn && data.TerritoryId > 0 ? data.TerritoryId : null,
            PlayerAvailable = available,
            Position = available && data.Position is { IsFinite: true } p ? p : null,
            JobId = available && data.JobId > 0 ? data.JobId : null,
            Level = available && data.Level is >= 1 and <= 1000 ? data.Level : null,
        };
        return new(data, data.LoggedIn ? context.CharacterToken : 0);
    }

    private static bool IsObservation(Inspector inspector) => inspector is Inspector.MovementSpeedTracker or Inspector.TargetDistanceTracker or Inspector.EntityNameWatch;

    private static bool SameContext(LiveContext a, LiveContext b) =>
        a.CharacterToken == b.CharacterToken && a.Data.LoggedIn == b.Data.LoggedIn &&
        a.Data.TerritoryId == b.Data.TerritoryId && a.Data.PlayerAvailable == b.Data.PlayerAvailable &&
        a.Data.Conditions.BetweenAreas == b.Data.Conditions.BetweenAreas;

    private void FailAll()
    {
        if (active != null) Finish(Outcome.Unavailable, Reason.SourceUnavailable, true);
        while (pending.TryDequeue(out var request))
        {
            active = new(request, clock.UtcNow, clock.Timestamp);
            Finish(Outcome.Unavailable, Reason.SourceUnavailable, false);
        }
    }

    private void CancelWork(Reason reason)
    {
        if (active != null) Finish(Outcome.Cancelled, reason, true);
        while (pending.TryDequeue(out var request))
        {
            active = new(request, clock.UtcNow, clock.Timestamp);
            Finish(Outcome.Cancelled, reason, true);
        }
    }

    private void Finish(Outcome outcome, Reason reason, bool partial)
    {
        var work = active!;
        active = null;
        if (work.AsyncCancellation != null)
        {
            work.AsyncCancellation.Cancel();
            work.AsyncCancellation.Dispose();
            if (work.AsyncTask is { } task)
                _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        if (work.ObservationStarted && tools is IObservationToolSource observer)
        {
            try { observer.EndObservation(work.Request.Inspector); }
            catch (Exception) { }
        }
        if (IsObservation(work.Request.Inspector) && outcome == Outcome.Cancelled) return;
        var request = work.Request;
        var result = new CaptureResult(request.Id, request.Inspector, Catalog.Get(request.Inspector).Version, request.Generation,
            request.Created, work.Started, clock.UtcNow, Math.Max(0, clock.ElapsedMilliseconds(work.Timestamp)),
            outcome, reason, partial, work.Steps > 1 || request.Inspector == Inspector.AddonCallbacks, work.Steps, work.Examined, work.Matched, work.Empty, work.Invalid, work.Filtered,
            work.Unavailable, work.Omitted, request.Options, work.Context, work.Rows.ToImmutableArray(), request.ToolOptions, work.Payload, work.HttpStatusCode);
        int size;
        try { size = ReportCodec.ResultSize(result, Performance); }
        catch (Exception) { dropped++; return; }
        while (size > limits.ResultBytes && !result.Objects.IsEmpty)
        {
            result = result with { Objects = result.Objects.RemoveAt(result.Objects.Length - 1),
                Omitted = result.Omitted + 1, Partial = true, Reason = Reason.PayloadLimit };
            size = ReportCodec.ResultSize(result, Performance);
        }
        while (size > limits.ResultBytes && result.Payload != null)
        {
            result = result with { Payload = Payloads.TrimLast(result.Payload), Omitted = result.Omitted + 1,
                Partial = true, Reason = Reason.PayloadLimit };
            size = ReportCodec.ResultSize(result, Performance);
        }
        if (size > limits.ResultBytes) { dropped++; return; }
        while (history.Count > 0 && (history.Count >= limits.Results || bytes + size > limits.SessionBytes))
        {
            bytes -= history[0].Bytes;
            history.RemoveAt(0);
            evicted++;
        }
        history.Add((result, size));
        bytes += size;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            generation++;
            CancelWork(Reason.Disposed);
            disposed = true;
            lastContext = null;
            Performance.Enabled = false;
            Performance.Clear();
        }
    }
}
