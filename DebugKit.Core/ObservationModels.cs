using System.Collections.Immutable;

namespace DebugKit.Core;

public static class ObservationPolicy
{
    public const int Capacity = 128;

    public static ObservationPayload Append(ObservationPayload previous, ObservationPayload next)
    {
        var combined = previous.Samples.AddRange(next.Samples);
        var overflow = Math.Max(0, combined.Length - Capacity);
        return next with
        {
            Samples = overflow == 0 ? combined : combined.RemoveRange(0, overflow),
            Dropped = previous.Dropped + next.Dropped + overflow,
        };
    }
}
