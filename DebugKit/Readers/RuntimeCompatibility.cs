using System.Diagnostics;
using System.Security.Cryptography;
using DebugKit.Core;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DebugKit.Readers;

internal sealed class RuntimeCompatibility
{
    public string GameBuild { get; }
    public bool Supported { get; }
    public string StructsVersion { get; } = "Unavailable";
    public string StructsHash { get; } = "Unavailable";
    public string Status => Supported ? "ExperimentalMatched" : "Unsupported";

    public RuntimeCompatibility()
    {
        GameBuild = "Unavailable";
        try
        {
            // Only the fixed version file beside the RUNNING executable; no directory search, path export, or user input.
            using var process = Process.GetCurrentProcess();
            var directory = Path.GetDirectoryName(process.MainModule?.FileName);
            if (directory != null)
            {
                using var file = File.OpenRead(Path.Combine(directory, "ffxivgame.ver"));
                if (file.Length is > 0 and <= 64)
                {
                    using var reader = new StreamReader(file);
                    GameBuild = NativeCompatibility.SafeBuild(reader.ReadToEnd().Trim());
                }
            }
            StructsVersion = typeof(AtkUnitBase).Assembly.GetName().Version?.ToString() ?? "Unavailable";
            using var assembly = File.OpenRead(typeof(AtkUnitBase).Assembly.Location);
            StructsHash = Convert.ToHexString(SHA256.HashData(assembly));
            Supported = NativeCompatibility.Matches(GameBuild, typeof(IDalamudPluginInterface).Assembly.GetName().Version?.ToString(), StructsHash);
        }
        catch (Exception) { Supported = false; }
    }
}
