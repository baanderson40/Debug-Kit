using System.Text;
using DebugKit.Core;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace DebugKit.Readers;

/// <summary>Detached, bounded menu-label decoding. Never evaluates game-dependent macros.</summary>
internal static class MenuLabelDecoder
{
    public const int MaxBytes = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length >= MaxBytes || bytes.Contains((byte)0)) return null;
        try
        {
            var label = new ReadOnlySeStringSpan(bytes);
            // Validate individual payloads; reject unknown/dynamic macros rather than silently losing text.
            foreach (var payload in label)
            {
                if (!payload.Validate()) return null;
                if (payload.Type == ReadOnlySePayloadType.Text)
                {
                    StrictUtf8.GetCharCount(payload.Body);
                    foreach (var value in payload.Body)
                        if (value < 0x20 || value == 0x7F) return null;
                }
                else if (payload.Type != ReadOnlySePayloadType.Macro || !PresentationOnly(payload.MacroCode))
                    return null;
            }
            return ObjectNames.For(ObjectCategory.EventNpc, label.ExtractText());
        }
        catch (Exception) { return null; }
    }

    private static bool PresentationOnly(MacroCode code) => code is
        MacroCode.Color or MacroCode.EdgeColor or MacroCode.ShadowColor or
        MacroCode.ColorType or MacroCode.EdgeColorType or MacroCode.Bold or MacroCode.Italic or
        MacroCode.Edge or MacroCode.Shadow or MacroCode.Scale or
        MacroCode.Icon or MacroCode.Icon2 or MacroCode.NewLine or
        MacroCode.NonBreakingSpace or MacroCode.Hyphen or MacroCode.SoftHyphen;
}
