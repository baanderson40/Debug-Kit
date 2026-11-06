using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text;

namespace DebugKit.Core;

public sealed record CallbackArgument(int Index, uint Type, bool? Boolean,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Signed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ulong? Unsigned = null);
public sealed record CallbackEntry(DateTimeOffset ObservedUtc, uint ArgumentCount, bool Flag, bool Returned,
    ImmutableArray<CallbackArgument> Arguments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Addon = null)
{
    public long ArgumentsOmitted => Math.Max(0, (long)ArgumentCount - Arguments.Length);
}
public sealed record CallbackPayload(string Addon, string GameBuild, DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc, ImmutableArray<CallbackEntry> Rows, long EventsDropped, int ReadFailures) : ToolPayload;

public interface ICallbackToolSource
{
    Task<ToolStep> StartCallbackCapture(ToolOptions options, CancellationToken cancellationToken);
    void RequestCallbackStop();
}

/// <summary>Detached callback evidence in a rolling buffer. Integer values are retained; strings/pointers are never accepted.</summary>
public sealed class CallbackBuffer
{
    public const int MaximumEvents = 64;
    public const int MaximumArguments = 16;
    private readonly object gate = new();
    private readonly List<CallbackEntry> rows = [];
    private long dropped;
    public long Dropped { get { lock (gate) return dropped; } }
    public bool HasDroppedEvents { get { lock (gate) return dropped > 0; } }
    public void Add(CallbackEntry entry)
    {
        lock (gate)
        {
            if (rows.Count == MaximumEvents)
            {
                rows.RemoveAt(0);
                if (dropped < long.MaxValue) dropped++;
            }
            rows.Add(Sanitize(entry));
        }
    }
    public ImmutableArray<CallbackEntry> Snapshot() { lock (gate) return rows.ToImmutableArray(); }
    public static CallbackEntry Sanitize(CallbackEntry entry) => entry with
    {
        Addon = CallbackNames.Clean(entry.Addon),
        Arguments = entry.Arguments.Take(MaximumArguments).Where(a => a.Index >= 0 && a.Index < entry.ArgumentCount)
            .Select(a => a with
            {
                Boolean = a.Type == 2 ? a.Boolean : null,
                Signed = a.Type == 4 || a.Type == 3 && a.Signed is >= int.MinValue and <= int.MaxValue ? a.Signed : null,
                Unsigned = a.Type == 6 || a.Type == 5 && a.Unsigned is <= uint.MaxValue ? a.Unsigned : null,
            }).ToImmutableArray(),
    };
}

public static class CallbackNames
{
    public static string? Clean(string? name) => name is { Length: >= 1 and <= 31 } &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name : null;

    // Native AtkUnitBase.Name is exactly 32 bytes. Never scan a C string or inspect an unseen tail.
    public static string? Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 32) return null;
        var bounded = buffer[..32];
        var length = bounded.IndexOf((byte)0);
        if (length is < 1 or > 31) return null;
        for (var i = 0; i < length; i++)
            if (!char.IsAsciiLetterOrDigit((char)bounded[i]) && bounded[i] != (byte)'_') return null;
        return Encoding.ASCII.GetString(bounded[..length]);
    }

    public static bool Matches(string filter, string? observed) => filter.Length == 0 || string.Equals(filter, observed, StringComparison.Ordinal);
    public static string Display(CallbackPayload payload, CallbackEntry entry) => entry.Addon ?? (payload.Addon.Length == 0 ? "unknown" : payload.Addon);
    public static string Scope(string filter) => filter.Length == 0 ? "all addons" : $"exact addon {filter}";
}

public static class CallbackText
{
    public const string NumericWarning = "Numeric callback arguments are unredacted and may contain character/object/owner or other IDs. Included in display, copy and export; review before sharing. String/pointer-typed arguments remain excluded.";
    public static string Boolean(bool value) => value ? "1" : "0";
    public static string TypeName(uint type) => type switch
    {
        0 => "Undefined", 1 => "Null", 2 => "Bool", 3 => "Int", 4 => "Int64", 5 => "UInt", 6 => "UInt64",
        7 => "Float", 8 => "String", 9 => "WideString", 10 => "ConstString", 11 => "Vector", 12 => "Pointer",
        13 => "AtkValues", 32 => "Managed", 40 => "ManagedString", 43 => "ManagedVector", _ => $"Type {type}",
    };
    public static string Argument(CallbackArgument argument)
    {
        var value = argument.Type switch
        {
            2 when argument.Boolean is { } boolean => Boolean(boolean),
            3 or 4 when argument.Signed is { } signed => signed.ToString(CultureInfo.InvariantCulture),
            5 or 6 when argument.Unsigned is { } unsigned => unsigned.ToString(CultureInfo.InvariantCulture),
            _ => "omitted",
        };
        return $"{argument.Index} [{TypeName(argument.Type)}]: {value}";
    }
}
