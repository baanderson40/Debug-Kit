using System.Reflection;
using Lumina.Excel.Sheets;
using DebugKit.Core;

if (args.Length == 2 && args[0] == "--familiar-sheets")
{
    using var game = new Lumina.GameData(args[1]);
    var raw = game.Excel.GetRawSheet("XBMPet", Lumina.Data.Language.English);
    for (var index = 0; index < raw.Columns.Count; index++)
        Console.WriteLine($"XBMPet column {index}: {raw.Columns[index].Type} offset {raw.Columns[index].Offset}");
    ProbeSheet<XBMPet>(game, row => $"Unknown0={row.Unknown0.ExtractText()}, Unknown1={row.Unknown1.ExtractText()}, Unknown2={row.Unknown2.ExtractText()}, Unknown3={row.Unknown3}, Unknown4={row.Unknown4}, Unknown5={row.Unknown5}, Unknown6={row.Unknown6}, Unknown7={row.Unknown7}, Unknown8={row.Unknown8}, Unknown9={row.Unknown9}, Unknown10={row.Unknown10}");
    ProbeSheet<XBMItem>(game, row => $"Unknown0={row.Unknown0.ExtractText()}, Unknown1={row.Unknown1.ExtractText()}, Unknown2={row.Unknown2.ExtractText()}, Unknown3={row.Unknown3.ExtractText()}, Unknown4={row.Unknown4.ExtractText()}, Unknown11={row.Unknown11}, Unknown12={row.Unknown12}, Unknown13={row.Unknown13}");
    ProbeSheet<XBMContent>(game, row => $"Unknown0={row.Unknown0}, Unknown1={row.Unknown1}, Unknown2={row.Unknown2}, Unknown3={row.Unknown3}, Unknown34={row.Unknown34}, Unknown35={row.Unknown35}, Unknown36={row.Unknown36}");
    ProbeSheet<DebugKit.Readers.XbmPetCurrentRow>(game, row => string.Join("; ", row.Fields().Select(f => $"{f.Field}={FieldText.Display(f)}")));
    foreach (var language in new[] { Lumina.Data.Language.English, Lumina.Data.Language.Japanese })
    {
        var sheet = game.Excel.GetSheet<DebugKit.Readers.XbmPetCurrentRow>(language);
        if (!sheet.TryGetRow(1, out var row) || row.Fields().Length != 12 ||
            !row.Fields().Any(f => f.Field == "Column08 (String)" && f.Value != "unavailable") ||
            sheet.TryGetRow(uint.MaxValue, out _)) throw new InvalidOperationException("Current XBMPet production reader verification failed");
        if (language == Lumina.Data.Language.English && !row.Fields().Any(f => f.Field == "Column08 (String)" && f.TextShortened && f.OriginalTextLength > 128))
            throw new InvalidOperationException("Current XBMPet description shortening was not disclosed");
    }
    Console.WriteLine("PASS current production XBMPet reader: hash validated, row 1 fields read in English/Japanese, missing-row lookup preserved. No job/player state involved.");
    return;
}

if (args.Length == 3 && args[0] == "--layout")
{
    using var game = new Lumina.GameData(args[1]);
    var raw = game.GetFile<Lumina.Data.FileResource>(args[2]) ?? throw new InvalidOperationException("Asset missing");
    var parsed = StaticLayoutParser.Parse(raw.Data) ?? throw new InvalidOperationException("Bounded parser rejected asset");
    var reference = game.GetFile<Lumina.Data.Files.LgbFile>(args[2]) ?? throw new InvalidOperationException("Lumina parser rejected asset");
    foreach (var entry in parsed.Entries)
    {
        var referenceEntry = reference.Layers[entry.Layer].InstanceObjects[entry.Entry];
        var expected = referenceEntry.Transform;
        if (entry.Position != new Point(expected.Translation.X, expected.Translation.Y, expected.Translation.Z) ||
            entry.Rotation != new Point(expected.Rotation.X, expected.Rotation.Y, expected.Rotation.Z) ||
            entry.Scale != new Point(expected.Scale.X, expected.Scale.Y, expected.Scale.Z))
            throw new InvalidOperationException("Placement transform mismatch");
        if (referenceEntry.Object is Lumina.Data.Parsing.Layer.LayerCommon.EventInstanceObject e && entry.BaseId != e.ParentData.BaseId)
            throw new InvalidOperationException("EventObject base ID mismatch");
        if (referenceEntry.Object is Lumina.Data.Parsing.Layer.LayerCommon.ENPCInstanceObject n && entry.BaseId != n.ParentData.ParentData.BaseId)
            throw new InvalidOperationException("EventNPC base ID mismatch");
        if (referenceEntry.Object is Lumina.Data.Parsing.Layer.LayerCommon.CollisionBoxInstanceObject c &&
            (entry.Shape != (int)c.ParentData.TriggerBoxShape || entry.Enabled != (c.ParentData.Enabled != 0)))
            throw new InvalidOperationException("Collision shape mismatch");
    }
    Console.WriteLine($"PASS installed static asset: {raw.Data.Length} bytes; {parsed.Entries.Length} bounded placements; partial={parsed.Partial}; transforms/reviewed fields match Lumina. Collision rows: {parsed.Entries.Count(e => e.Type == "CollisionBox")}; base-ID rows: {parsed.Entries.Count(e => e.BaseId != null)}.");
    return;
}

foreach (var type in typeof(Item).Assembly.GetExportedTypes().Where(t => t.Namespace == "Lumina.Excel.Sheets" &&
    (t.Name.StartsWith("XBM", StringComparison.Ordinal) || t.Name.StartsWith("Housing", StringComparison.Ordinal) ||
     t.Name is "ContentFinderCondition" or "DynamicEvent" or "Fate" or "TerritoryType" or "RetainerTask")))
    Console.WriteLine($"{type.Name}: {string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{p.Name}:{p.PropertyType.Name}"))}");

static void ProbeSheet<T>(Lumina.GameData game, Func<T, string> fields) where T : struct, Lumina.Excel.IExcelRow<T>
{
    foreach (var language in new[] { Lumina.Data.Language.English, Lumina.Data.Language.Japanese, Lumina.Data.Language.None })
    {
        var stage = "load";
        try
        {
            var sheet = game.Excel.GetSheet<T>(language);
            Console.WriteLine($"{typeof(T).Name}/{language}: loaded actual={sheet.Language}, count={sheet.Count}");
            stage = "row";
            if (!sheet.TryGetRow(1, out var row)) { Console.WriteLine("Row 1 missing"); continue; }
            stage = "fields";
            Console.WriteLine(fields(row));
        }
        catch (Exception exception) { Console.WriteLine($"{typeof(T).Name}/{language}: {stage}: {exception.GetType().Name}: {exception.Message}"); }
    }
}
