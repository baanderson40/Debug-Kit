using Dalamud.Game.Command;
using Dalamud.Game.ClientState;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using DebugKit.Readers;
using DebugKit.Readers.Native;

namespace DebugKit;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/debugkit";
    private const string Alias = "/dk";
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly WindowSystem windows = new("DebugKit");
    private readonly DiagnosticEngine engine;
    private readonly ReportExporter exporter;
    private readonly MainWindow window;
    private readonly NativeReader native;
    private readonly DomainReader domains;
    private readonly NetworkCaptureClient network;
    private readonly AddonCallbackObserver callbacks;
    private readonly CommandInfo commandInfo;
    private bool commandOwned, aliasOwned, disposed;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework,
        IClientState client, IPlayerState player, IObjectTable objects, ICondition conditions,
        ITargetManager targets, IDataManager data, IGameGui gui, IAddonLifecycle lifecycle, IFateTable fates, IGameInteropProvider interop)
    {
        this.pi = pi;
        this.commands = commands;
        this.framework = framework;
        this.client = client;
        Configuration configuration;
        try { configuration = pi.GetPluginConfig() as Configuration ?? new(); }
        catch (Exception) { configuration = new(); }
        configuration.Validate();
        var compatibility = new RuntimeCompatibility();
        var sheets = new SheetReader(data, compatibility);
        native = new(framework, gui, lifecycle, compatibility, sheets);
        domains = new(framework, client, objects, targets, fates, data, compatibility, sheets, native);
        network = new();
        callbacks = new(framework, interop, compatibility);
        engine = new(new DalamudGameSource(framework, client, player, objects, conditions, targets, sheets, native, domains, new(pi), network, callbacks));
        exporter = new(Path.Combine(pi.ConfigDirectory.FullName, "reports"));
        var provenance = Provenance.Create(typeof(Plugin).Assembly.GetName().Version?.ToString(),
            typeof(IDalamudPluginInterface).Assembly.GetName().Version?.ToString(), compatibility.GameBuild, compatibility.Status,
            compatibility.StructsVersion, compatibility.StructsHash);
        window = new(engine, exporter, provenance, configuration, () => pi.SavePluginConfig(configuration));
        commandInfo = new(OnCommand) { HelpMessage = "Open Debug Kit's standalone read-only inspectors." };
        try
        {
            windows.AddWindow(window);
            // AddHandler returns false on conflict; never overwrite or later remove somebody else's command.
            if (!commands.Commands.ContainsKey(Command)) commandOwned = commands.AddHandler(Command, commandInfo);
            if (!commands.Commands.ContainsKey(Alias)) aliasOwned = commands.AddHandler(Alias, commandInfo);
            engine.SetCommandAvailable(commandOwned);
            pi.UiBuilder.Draw += Draw;
            pi.UiBuilder.OpenMainUi += Open;
            pi.UiBuilder.OpenConfigUi += Open;
            client.TerritoryChanged += OnTerritoryChanged;
            client.InstanceChanged += OnTerritoryChanged;
            client.ZoneInit += OnZoneInit;
            client.Login += OnLogin;
            client.Logout += OnLogout;
            framework.Update += OnUpdate;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnCommand(string command, string args) => Open();
    private void Open() { if (!disposed) window.IsOpen = true; }
    private void Draw() { if (!disposed) windows.Draw(); }
    private void OnUpdate(IFramework _)
    {
        if (disposed) return;
        callbacks.Update(); engine.Tick(); callbacks.Update();
    }
    private void OnTerritoryChanged(uint _) => engine.Invalidate();
    private void OnZoneInit(ZoneInitEventArgs _) => engine.Invalidate();
    private void OnLogin() => engine.Invalidate();
    private void OnLogout(int type, int code) => engine.Invalidate();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        framework.Update -= OnUpdate;
        client.TerritoryChanged -= OnTerritoryChanged;
        client.InstanceChanged -= OnTerritoryChanged;
        client.ZoneInit -= OnZoneInit;
        client.Login -= OnLogin;
        client.Logout -= OnLogout;
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenMainUi -= Open;
        pi.UiBuilder.OpenConfigUi -= Open;
        if (commandOwned && commands.Commands.TryGetValue(Command, out var registered) && ReferenceEquals(registered, commandInfo))
            commands.RemoveHandler(Command);
        if (aliasOwned && commands.Commands.TryGetValue(Alias, out var registeredAlias) && ReferenceEquals(registeredAlias, commandInfo))
            commands.RemoveHandler(Alias);
        windows.RemoveAllWindows();
        window.Dispose();
        exporter.Dispose();
        engine.Dispose();
        callbacks.Dispose();
        network.Dispose();
        domains.Context(0);
        native.Dispose();
    }
}
