using System.Collections.Immutable;
using Dalamud.Plugin;
using DebugKit.Core;

namespace DebugKit.Readers;

/// <summary>Invoked only by the framework-thread source; copies fixed public metadata, never plugin instances/config.</summary>
internal sealed class ProviderReader(IDalamudPluginInterface pi)
{
    public ToolStep Query(ProviderKind kind) => ProviderQueries.Capture(kind, () => (ProviderPayload)Metadata().Payload!, contract =>
    {
        // Endpoint comes exclusively from the fixed, source-audited registry. No subscriptions or actions.
        var subscriber = pi.GetIpcSubscriber<bool>(contract.Endpoint);
        return subscriber.HasFunction ? subscriber.InvokeFunc() : null;
    });

    public ToolStep Metadata()
    {
        var rows = new List<ProviderMetadataSample>();
        var complete = true;
        var examined = 0;
        try
        {
            foreach (var plugin in pi.InstalledPlugins)
            {
                if (examined++ >= 512) { complete = false; break; }
                var name = plugin.InternalName;
                if (!ProviderCatalog.All.Any(x => x.InternalName == name)) continue;
                try { rows.Add(new(name, plugin.Version.ToString(), plugin.IsLoaded, plugin.IsDev)); }
                catch (Exception) { complete = false; }
            }
        }
        catch (Exception) { complete = false; }
        var payload = ProviderCatalog.Metadata(rows, complete);
        return new(payload, Partial: !complete, Reason: complete ? Reason.None : Reason.SourceUnavailable,
            Examined: Math.Min(examined, 512), Matched: rows.Count,
            Unavailable: complete ? 0 : 1);
    }

    public ToolStep InstalledPlugins()
    {
        var rows = new List<PluginInventoryRow>();
        var complete = true;
        var examined = 0;
        try
        {
            foreach (var plugin in pi.InstalledPlugins)
            {
                if (examined++ >= 512) { complete = false; break; }
                try
                {
                    var name = plugin.InternalName;
                    if (name is { Length: > 0 and <= 96 } && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
                        rows.Add(new(name, plugin.IsLoaded, ProviderCatalog.SafeVersion(plugin.Version.ToString()) ?? "Unavailable"));
                    else complete = false;
                }
                catch (Exception) { complete = false; }
            }
        }
        catch (Exception) { complete = false; }
        var payload = new PluginInventoryPayload(rows.OrderBy(x => x.InternalName, StringComparer.OrdinalIgnoreCase).ToImmutableArray(), complete);
        return new(payload, Partial: !complete, Reason: complete ? Reason.None : Reason.SourceUnavailable,
            Examined: Math.Min(examined, 512), Matched: rows.Count, Unavailable: complete ? 0 : 1);
    }
}
