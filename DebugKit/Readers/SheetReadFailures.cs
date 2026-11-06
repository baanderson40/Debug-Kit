using DebugKit.Core;
using Lumina.Excel.Exceptions;

namespace DebugKit.Readers;

internal static class SheetReadFailures
{
    public static ToolStep Loading(Exception exception) => exception switch
    {
        MismatchedColumnHashException => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SheetSchemaMismatch),
        UnsupportedLanguageException => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SheetLanguageUnavailable),
        SheetNotFoundException => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SheetUnavailable),
        NotSupportedException => new(null, Outcome: Outcome.Unavailable, Reason: Reason.SheetVariantUnsupported),
        _ => new(null, Outcome: Outcome.Failed, Reason: Reason.SheetUnavailable),
    };

    public static ToolStep Fields() => new(null, Outcome: Outcome.Failed, Reason: Reason.SheetFieldReadFailed,
        Partial: true, Examined: 1, Unavailable: 1);
}
