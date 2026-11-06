using System.Collections.Immutable;
using DebugKit.Core;
using Lumina.Excel;
using static DebugKit.Core.DomainPolicies;

namespace DebugKit.Readers;

// Installed EXH column types/offsets reviewed 2026-11-05. Do not relax the hash or infer column semantics.
[Sheet("XBMPet", SchemaHash)]
internal readonly struct XbmPetCurrentRow(ExcelPage page, uint offset, uint row) : IExcelRow<XbmPetCurrentRow>
{
    public const uint SchemaHash = 0xED3C4185;
    public ExcelPage ExcelPage => page;
    public uint RowOffset => offset;
    public uint RowId => row;
    public static XbmPetCurrentRow Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);

    public ImmutableArray<FieldRow> Fields() =>
    [
        F("SchemaProfile", "XBMPet ED3C4185"),
        F("Column00 (Int32)", page.ReadInt32(offset + 16)),
        F("Column01 (UInt8)", page.ReadUInt8(offset + 24)),
        F("Column02 (UInt8)", page.ReadUInt8(offset + 25)),
        F("Column03 (UInt8)", page.ReadUInt8(offset + 26)),
        F("Column04 (UInt32)", page.ReadUInt32(offset + 12)),
        F("Column05 (UInt16)", page.ReadUInt16(offset + 20)),
        F("Column06 (UInt8)", page.ReadUInt8(offset + 27)),
        F("Column07 (UInt16)", page.ReadUInt16(offset + 22)),
        F("Column08 (String)", page.ReadString(offset, offset).ExtractText()),
        F("Column09 (String)", page.ReadString(offset + 4, offset).ExtractText()),
        F("Column10 (String)", page.ReadString(offset + 8, offset).ExtractText()),
    ];
}
