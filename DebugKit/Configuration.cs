using Dalamud.Configuration;

namespace DebugKit;

// Only Debug Kit UI preferences. No source-plugin config or automation settings.
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public float Radius { get; set; } = 60;

    public void Validate()
    {
        Version = 1;
        Radius = float.IsFinite(Radius) ? Math.Clamp(Radius, 1, 200) : 60;
    }
}
