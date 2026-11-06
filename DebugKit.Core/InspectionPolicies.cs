namespace DebugKit.Core;

/// <summary>Managed guard; native readers use it only within one call, never across updates.</summary>
public sealed class TraversalGuard<T> where T : notnull
{
    private readonly HashSet<T> visited = new();
    private readonly int maxDepth, maxNodes;
    public bool Limited { get; private set; }
    public bool Cycle { get; private set; }
    public int Count => visited.Count;

    public TraversalGuard(int maxDepth = 8, int maxNodes = 256)
    {
        if (maxDepth is < 1 or > 8 || maxNodes is < 1 or > 256) throw new ArgumentOutOfRangeException();
        this.maxDepth = maxDepth; this.maxNodes = maxNodes;
    }
    public bool TryVisit(T value, int depth)
    {
        if (depth < 0 || depth > maxDepth || visited.Count >= maxNodes) { Limited = true; return false; }
        if (!visited.Add(value)) { Cycle = true; Limited = true; return false; }
        return true;
    }
}

public static class InventoryPolicy
{
    public static bool Ready(bool loaded, bool hasItems, int size) => loaded && hasItems && size is >= 1 and <= 256;
    public static bool Matches(ToolOptions options, uint itemId, bool hq) =>
        (options.ItemId == null || options.ItemId == itemId) &&
        (options.Quality == ItemQuality.Any || (options.Quality == ItemQuality.Hq ? hq : !hq));
}
