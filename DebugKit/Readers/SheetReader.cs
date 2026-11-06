using System.Collections.Immutable;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using DebugKit.Core;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace DebugKit.Readers;

internal sealed class SheetReader(IDataManager data, RuntimeCompatibility compatibility)
{
    public string ClientLanguageName => data.Language.ToString();
    public ToolStep Capture(ToolOptions options)
    {
        ClientLanguage? language = options.Language switch
        {
            DataLanguage.Client => null, DataLanguage.English => ClientLanguage.English,
            DataLanguage.Japanese => ClientLanguage.Japanese, DataLanguage.German => ClientLanguage.German,
            DataLanguage.French => ClientLanguage.French, _ => null,
        };
        return options.Sheet switch
        {
            SheetKind.Item => Read<Item>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("Icon", row.Icon), F("StackSize", row.StackSize),
              F("PriceMid", row.PriceMid), F("PriceLow", row.PriceLow), F("CanBeHq", row.CanBeHq),
              F("ItemUICategory.RowId", row.ItemUICategory.RowId) ]),
            SheetKind.Action => Read<Lumina.Excel.Sheets.Action>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("Icon", row.Icon), F("ClassJob.RowId", row.ClassJob.RowId),
              F("ClassJobLevel", row.ClassJobLevel), F("Range", row.Range) ]),
            SheetKind.Mount => Read<Mount>(options, language, row =>
            [ F("Singular", row.Singular.ExtractText()), F("Plural", row.Plural.ExtractText()), F("Icon", row.Icon) ]),
            SheetKind.TerritoryType => Read<TerritoryType>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("PlaceName.RowId", row.PlaceName.RowId),
              F("PlaceName.Name", row.PlaceName.RowId == 0 ? null : data.GetExcelSheet<PlaceName>(language)?.GetRowOrDefault(row.PlaceName.RowId)?.Name.ExtractText()),
              F("Map.RowId", row.Map.RowId) ]),
            SheetKind.BNpcName => Read<BNpcName>(options, language, row =>
            [ F("Singular", row.Singular.ExtractText()), F("Plural", row.Plural.ExtractText()) ]),
            SheetKind.ENpcResident => Read<ENpcResident>(options, language, row =>
            [ F("Singular", row.Singular.ExtractText()), F("Plural", row.Plural.ExtractText()) ]),
            SheetKind.EObj => Read<EObj>(options, language, row =>
            [ F("Data", row.Data), F("PopType", row.PopType) ]),
            SheetKind.ClassJob => Read<ClassJob>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("Abbreviation", row.Abbreviation.ExtractText()) ]),
            SheetKind.PlaceName => Read<PlaceName>(options, language, row =>
            [ F("Name", row.Name.ExtractText()) ]),
            SheetKind.Fate => Read<Lumina.Excel.Sheets.Fate>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("ClassJobLevel", row.ClassJobLevel), F("ClassJobLevelMax", row.ClassJobLevelMax),
              F("Icon", row.Icon), F("MapIcon", row.MapIcon), F("RequiredQuest.RowId", row.RequiredQuest.RowId), F("FATEChain", row.FATEChain) ]),
            SheetKind.DynamicEvent => Read<DynamicEvent>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("LGBEventObject", row.LGBEventObject), F("LGBMapRange", row.LGBMapRange),
              F("EventType.RowId", row.EventType.RowId), F("MaxParticipants", row.MaxParticipants), F("Duration (raw sheet units)", row.Duration) ]),
            SheetKind.ContentFinderCondition => Read<ContentFinderCondition>(options, language, row =>
            [ F("Name", row.Name.ExtractText()), F("TerritoryType.RowId", row.TerritoryType.RowId), F("ContentType.RowId", row.ContentType.RowId),
              F("ClassJobLevelRequired", row.ClassJobLevelRequired), F("ClassJobLevelSync", row.ClassJobLevelSync),
              F("ItemLevelRequired", row.ItemLevelRequired), F("ItemLevelSync", row.ItemLevelSync), F("QueueMaxPlayers (raw; not party size)", row.QueueMaxPlayers),
              F("AllowUndersized", row.AllowUndersized), F("IsInDutyFinder", row.IsInDutyFinder), F("PvP", row.PvP) ]),
            SheetKind.XBMPet => Read<XbmPetCurrentRow>(options, language, row => row.Fields()),
            SheetKind.XBMItem => Read<XBMItem>(options, language, row =>
            [ F("Unknown0", row.Unknown0.ExtractText()), F("Unknown1", row.Unknown1.ExtractText()), F("Unknown2", row.Unknown2.ExtractText()),
              F("Unknown3", row.Unknown3.ExtractText()), F("Unknown4", row.Unknown4.ExtractText()), F("Unknown11", row.Unknown11),
              F("Unknown12", row.Unknown12), F("Unknown13", row.Unknown13) ]),
            SheetKind.XBMContent => Read<XBMContent>(options, language, row =>
            [ F("Unknown0", row.Unknown0), F("Unknown1", row.Unknown1), F("Unknown2", row.Unknown2), F("Unknown3", row.Unknown3),
              F("Unknown34", row.Unknown34), F("Unknown35", row.Unknown35), F("Unknown36", row.Unknown36) ]),
            SheetKind.HousingPlacement => Read<HousingPlacement>(options, language, row => [F("Text", row.Text.ExtractText())]),
            SheetKind.HousingUnplacement => Read<HousingUnplacement>(options, language, row => [F("Text", row.Text.ExtractText())]),
            SheetKind.RetainerTask => Read<RetainerTask>(options, language, row =>
            [ F("Experience", row.Experience), F("VentureCost", row.VentureCost), F("MaxTimemin (sheet maximum, not live timer)", row.MaxTimemin),
              F("RetainerLevel", row.RetainerLevel), F("RequiredItemLevel", row.RequiredItemLevel), F("RequiredGathering", row.RequiredGathering),
              F("ClassJobCategory.RowId", row.ClassJobCategory.RowId), F("IsRandom", row.IsRandom) ]),
            _ => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SourceUnavailable),
        };
    }

    public ToolStep CaptureJobList(ToolOptions options)
    {
        ClientLanguage? language = options.Language switch
        {
            DataLanguage.Client => null, DataLanguage.English => ClientLanguage.English,
            DataLanguage.Japanese => ClientLanguage.Japanese, DataLanguage.German => ClientLanguage.German,
            DataLanguage.French => ClientLanguage.French, _ => null,
        };
        ExcelSheet<ClassJob> sheet;
        try { sheet = data.GetExcelSheet<ClassJob>(language); }
        catch (Exception exception) { return SheetReadFailures.Loading(exception); }
        var rows = ImmutableArray.CreateBuilder<LookupRow>();
        var examined = 0;
        try
        {
            for (uint id = 1; id <= 128; id++)
            {
                examined++;
                if (!sheet.TryGetRow(id, out var row)) continue;
                rows.Add(new(id, [F("Name", row.Name.ExtractText()), F("Abbreviation", row.Abbreviation.ExtractText())]));
            }
        }
        catch (Exception) { return new(new LookupPayload(Inspector.JobLookup, sheet.Language.ToString(), rows.ToImmutable(), false, "ClassJob IDs 1–128"),
            Partial: true, Reason: Reason.SheetFieldReadFailed, Examined: examined, Matched: rows.Count, Unavailable: 1); }
        return new(new LookupPayload(Inspector.JobLookup, sheet.Language.ToString(), rows.ToImmutable(), true, "ClassJob IDs 1–128; bounded scan"),
            Examined: examined, Matched: rows.Count);
    }

    private ToolStep Read<T>(ToolOptions options, ClientLanguage? language, Func<T, ImmutableArray<FieldRow>> fields)
        where T : struct, IExcelRow<T>
    {
        ExcelSheet<T> sheet;
        try { sheet = data.GetExcelSheet<T>(language); }
        catch (Exception exception) { return SheetReadFailures.Loading(exception); }
        try
        {
            if (!sheet.TryGetRow(options.RowId, out var row)) return new(null, Outcome: Outcome.Unavailable, Reason: Reason.RowNotFound);
            return new(new SheetPayload(options.Sheet, options.RowId, sheet.Language.ToString(), fields(row), compatibility.GameBuild),
                Examined: 1, Matched: 1);
        }
        catch (Exception) { return SheetReadFailures.Fields(); }
    }

    private static FieldRow F(string label, object? value) => FieldText.Create(label, value is IFormattable f
        ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString());

    public string? ItemName(uint id)
    {
        try { return ObjectNames.For(ObjectCategory.EventNpc, data.GetExcelSheet<Item>().GetRowOrDefault(id)?.Name.ExtractText()); }
        catch (Exception) { return null; }
    }

    public string? ActionName(uint id, byte type)
    {
        if (type != 1) return null; // Other action-type ID spaces are not the Action sheet.
        try { return ObjectNames.For(ObjectCategory.EventNpc, data.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id)?.Name.ExtractText()); }
        catch (Exception) { return null; }
    }
}
