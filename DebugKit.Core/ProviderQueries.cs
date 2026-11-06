namespace DebugKit.Core;

/// <summary>Fail-closed policy around one audited boolean IPC observation. Null means endpoint absent.</summary>
public static class ProviderQueries
{
    public static ToolStep Capture(ProviderKind kind, Func<ProviderPayload> metadata, Func<ProviderContract, bool?> invoke)
    {
        var snapshot = metadata();
        var row = snapshot.Rows.First(x => x.Provider == kind);
        if (!snapshot.EnumerationComplete || row.Installed != ObservationState.Yes || row.Loaded != ObservationState.Yes)
            return new(new ProviderPayload([row], snapshot.EnumerationComplete, "No IPC invoked; provider unavailable"),
                Outcome: Outcome.Unavailable, Reason: Reason.ProviderUnavailable);
        var contract = ProviderCatalog.Contract(row);
        if (contract == null) return new(new ProviderPayload([row], snapshot.EnumerationComplete, "No IPC invoked; contract unsupported"),
            Outcome: Outcome.Blocked, Reason: Reason.UnsupportedContract);
        try
        {
            var value = invoke(contract);
            if (value == null) return new(new ProviderQueryPayload(row with { EndpointPresent = ObservationState.No },
                contract.Query, null, contract.Endpoint), Outcome: Outcome.Unavailable, Reason: Reason.IpcMissing);
            var afterSnapshot = metadata();
            var after = afterSnapshot.Rows.First(x => x.Provider == kind);
            if (!afterSnapshot.EnumerationComplete || after != row) return new(null, Outcome: Outcome.Cancelled, Reason: Reason.SourceChanged, Partial: true);
            return new(new ProviderQueryPayload(row with { EndpointPresent = ObservationState.Yes, Responding = ObservationState.Yes,
                Ready = contract.Query is ProviderQuery.NavmeshReady or ProviderQuery.IpcReady ? value : null }, contract.Query, value, contract.Endpoint), Examined: 1, Matched: 1);
        }
        catch (Exception)
        {
            return new(new ProviderQueryPayload(row with { Responding = ObservationState.No }, contract.Query, null, contract.Endpoint),
                Outcome: Outcome.Unavailable, Reason: Reason.IpcFailed, Unavailable: 1);
        }
    }
}
