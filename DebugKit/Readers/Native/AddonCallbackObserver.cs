using System.Collections.Immutable;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DebugKit.Readers.Native;

/// <summary>Only the explicitly armed FireCallback observer. No callback dispatch or argument writes.</summary>
internal sealed unsafe class AddonCallbackObserver(IFramework framework, IGameInteropProvider interop,
    RuntimeCompatibility compatibility) : IDisposable
{
    private sealed class Session(string addon, CancellationToken cancellation, IClock clock)
    {
        public readonly string Addon = addon;
        public readonly CancellationToken Cancellation = cancellation;
        public readonly DateTimeOffset StartedUtc = clock.UtcNow;
        public readonly CallbackBuffer Buffer = new();
        public readonly TaskCompletionSource<ToolStep> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopRequested, Attempts, Failed;
    }
    private readonly IClock clock = new SystemClock();
    private Hook<AtkUnitBase.Delegates.FireCallback>? hook;
    private volatile Session? session;
    private bool disposed;

    public Task<ToolStep> Start(ToolOptions options, CancellationToken cancellation)
    {
        if (disposed || !framework.IsInFrameworkUpdateThread) throw new InvalidOperationException();
        if (!compatibility.Supported) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Unavailable, Reason: Reason.UnsupportedBuild));
        if (!options.ExperimentalNative) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Blocked, Reason: Reason.NativeOptInRequired));
        if (!options.IsValid(Inspector.AddonCallbacks)) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Blocked, Reason: Reason.InvalidRange));
        if (session != null) End(true);
        if (cancellation.IsCancellationRequested) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Cancelled, Reason: Reason.UserCancelled));
        // Generated signature/address from the exact hash-matched installed ClientStructs, not copied signature scans.
        if (hook == null)
        {
            var address = (nint)AtkUnitBase.MemberFunctionPointers.FireCallback;
            if (address == 0) return Task.FromResult(new ToolStep(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable));
            hook = interop.HookFromAddress<AtkUnitBase.Delegates.FireCallback>(address, Detour);
        }
        var capture = new Session(options.Addon, cancellation, clock);
        session = capture;
        try { hook.Enable(); }
        catch { session = null; hook.Dispose(); hook = null; throw; }
        return capture.Completion.Task;
    }

    public void RequestCallbackStop()
    {
        if (session is { } capture) Volatile.Write(ref capture.StopRequested, 1);
    }

    public void Update()
    {
        if (disposed || session is not { } capture) return;
        if (!framework.IsInFrameworkUpdateThread) throw new InvalidOperationException();
        if (capture.Cancellation.IsCancellationRequested) End(true);
        else if (Volatile.Read(ref capture.StopRequested) != 0) End(false);
    }

    private bool Detour(AtkUnitBase* addon, uint count, AtkValue* values, bool flag)
    {
        var capture = session;
        CallbackEntry? entry = null;
        try
        {
            if (!disposed && capture != null && !capture.Cancellation.IsCancellationRequested &&
                framework.IsInFrameworkUpdateThread && addon != null)
            {
                var addonName = CallbackNames.Read(addon->Name);
                if (CallbackNames.Matches(capture.Addon, addonName))
                {
                    if (capture.Attempts < int.MaxValue) capture.Attempts++;
                    var arguments = ImmutableArray.CreateBuilder<CallbackArgument>();
                    // Excessive/missing buffers are not dereferenced. Integer values are always enabled for this user-requested logger.
                    if (values != null && count <= 128)
                        for (var i = 0; i < Math.Min(count, (uint)CallbackBuffer.MaximumArguments); i++)
                        {
                            var type = values[i].Type;
                            long? signed = null;
                            ulong? unsigned = null;
                            if (type == AtkValueType.Int) signed = values[i].Int;
                            else if (type == AtkValueType.Int64) signed = values[i].Int64;
                            else if (type == AtkValueType.UInt) unsigned = values[i].UInt;
                            else if (type == AtkValueType.UInt64) unsigned = values[i].UInt64;
                            arguments.Add(new(i, (uint)type, type == AtkValueType.Bool ? values[i].Bool : null, signed, unsigned));
                        }
                    entry = new(clock.UtcNow, count, flag, false, arguments.ToImmutable(), addonName ?? "unknown");
                }
            }
        }
        catch { if (capture != null && capture.Failed < int.MaxValue) capture.Failed++; }
        // Exactly one call, unchanged arguments and unchanged return. Observer exceptions never cause a retry.
        var returned = hook!.Original(addon, count, values, flag);
        try
        {
            if (entry != null && capture != null && !capture.Cancellation.IsCancellationRequested && ReferenceEquals(session, capture))
                capture.Buffer.Add(entry with { Returned = returned });
        }
        catch { if (capture != null && capture.Failed < int.MaxValue) capture.Failed++; }
        return returned;
    }

    private void End(bool cancelled)
    {
        var capture = session;
        session = null; // Immediately stop retention before disabling; no native pointers survive a detour.
        var stopFailed = false;
        try { hook?.Disable(); }
        catch
        {
            stopFailed = true;
            try { hook?.Dispose(); hook = null; }
            catch { /* Retain the managed hook owner for another disposal attempt; no observations accepted. */ }
        }
        if (capture == null) return;
        if (cancelled) { capture.Completion.TrySetResult(new(null, Outcome: Outcome.Cancelled, Reason: Reason.UserCancelled)); return; }
        var rows = capture.Buffer.Snapshot();
        if (stopFailed && capture.Failed < int.MaxValue) capture.Failed++;
        var argumentsOmitted = rows.Any(r => r.ArgumentsOmitted > 0);
        capture.Completion.TrySetResult(new(new CallbackPayload(capture.Addon, compatibility.GameBuild,
            capture.StartedUtc, clock.UtcNow, rows, capture.Buffer.Dropped, capture.Failed), Partial: capture.Buffer.Dropped > 0 || argumentsOmitted || capture.Failed > 0,
            Outcome: capture.Failed > 0 ? Outcome.Unavailable : Outcome.Success,
            Reason: capture.Failed > 0 ? Reason.SourceUnavailable : capture.Buffer.Dropped > 0 ? Reason.RowLimit : argumentsOmitted ? Reason.EntryLimit : Reason.None,
            Examined: capture.Attempts, Matched: rows.Length, Unavailable: capture.Failed));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        End(true);
        hook?.Dispose();
        hook = null;
    }
}
