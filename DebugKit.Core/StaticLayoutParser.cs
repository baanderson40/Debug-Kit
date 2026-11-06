using System.Buffers.Binary;
using System.Collections.Immutable;

namespace DebugKit.Core;

public sealed record StaticPlacement(int Layer, int Entry, string Type, uint? BaseId, Point Position, Point Rotation, Point Scale,
    int? Shape, bool? Enabled);
public sealed record StaticLayout(ImmutableArray<StaticPlacement> Entries, bool Partial);

/// <summary>Bounded LGB1/LGP1 common placement decoder. Does not parse names, asset references or nested payloads.</summary>
public static class StaticLayoutParser
{
    public const int MaxBytes = 4 * 1024 * 1024, MaxLayers = 128, MaxEntries = 1024;
    public static StaticLayout? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 36 or > MaxBytes || !bytes[..4].SequenceEqual("LGB1"u8) || !bytes.Slice(12, 4).SequenceEqual("LGP1"u8)) return null;
        try
        {
            var layers = I32(bytes, 32);
            if (layers is < 0 or > MaxLayers || !Range(bytes, 36, (long)layers * 4)) return null;
            var rows = ImmutableArray.CreateBuilder<StaticPlacement>();
            for (var layer = 0; layer < layers; layer++)
            {
                var offset = Relative(bytes, 36, I32(bytes, 36 + layer * 4), 16);
                var count = I32(bytes, offset + 12);
                if (count is < 0 or > 65535) return null;
                var entries = Relative(bytes, offset, I32(bytes, offset + 8), (long)count * 4);
                for (var index = 0; index < count; index++)
                {
                    if (rows.Count == MaxEntries) return new(rows.ToImmutable(), true);
                    var entry = Relative(bytes, entries, I32(bytes, entries + index * 4), 48);
                    var type = I32(bytes, entry);
                    var position = PointAt(bytes, entry + 12); var rotation = PointAt(bytes, entry + 24); var scale = PointAt(bytes, entry + 36);
                    if (!position.IsFinite || !rotation.IsFinite || !scale.IsFinite) return null;
                    uint? baseId = null; int? shape = null; bool? enabled = null;
                    if (type is 8 or 9 or 45)
                    {
                        if (!Range(bytes, entry + 48, 4)) return null;
                        baseId = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(entry + 48)..]);
                    }
                    if (type is 49 or 57)
                    {
                        if (!Range(bytes, entry + 48, 12)) return null;
                        shape = I32(bytes, entry + 48); enabled = bytes[entry + 54] != 0;
                    }
                    rows.Add(new(layer, index, TypeName(type), baseId, position, rotation, scale, shape, enabled));
                }
            }
            return new(rows.ToImmutable(), false);
        }
        catch (ArgumentException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static bool Range(ReadOnlySpan<byte> data, long offset, long size) => offset >= 0 && size >= 0 && offset + size <= data.Length;
    private static int Relative(ReadOnlySpan<byte> data, int origin, int relative, long size)
    {
        var offset = (long)origin + relative;
        if (relative < 0 || !Range(data, offset, size)) throw new ArgumentOutOfRangeException(nameof(relative));
        return checked((int)offset);
    }
    private static int I32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static Point PointAt(ReadOnlySpan<byte> bytes, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4)), BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 4, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 8, 4)));
    private static string TypeName(int type) => type switch
    {
        8 => "EventNPC", 9 => "BattleNPC", 16 => "Treasure", 45 => "EventObject", 49 => "EventRange", 57 => "CollisionBox",
        _ => "Other static placement",
    };
}
