using System.Collections.Immutable;

namespace DebugKit.Core;

public enum NetworkService { Universalis, PaissaDb }
public enum MarketScope { World, DataCenter }
public enum MarketDataCenter { Aether, Primal, Crystal, Dynamis, Chaos, Light, Materia, Elemental, Gaia, Mana, Meteor }
public enum HousingDistrict { Mist = 339, LavenderBeds = 340, Goblet = 341, Shirogane = 641, Empyreum = 979 }
public sealed record NetworkPayload(NetworkService Service, DateTimeOffset RetrievedUtc, ImmutableArray<EvidenceRow> Rows) : ToolPayload;

public interface IAsyncToolSource
{
    // Called once, on framework thread. Returned task must never read game/provider state.
    Task<ToolStep> StartNetworkCapture(Inspector inspector, ToolOptions options, CancellationToken cancellationToken);
}

public static class NetworkPolicy
{
    public const int MaximumResponseBytes = 131072;
    public static bool IsNetwork(Inspector inspector) => inspector is Inspector.MarketSummary or Inspector.HousingService;
    public static bool Valid(Inspector inspector, ToolOptions options) => options.NetworkConsent &&
        (inspector == Inspector.MarketSummary
            ? options.ItemId is >= 1 and <= 10000000 && Enum.IsDefined(options.MarketScope) &&
                (options.MarketScope == MarketScope.DataCenter ? Enum.IsDefined(options.DataCenter) : options.WorldId is >= 1 and <= 65535)
            : inspector == Inspector.HousingService && options.WorldId is >= 1 and <= 65535 && Enum.IsDefined(options.District));

    public static Uri RequestUri(Inspector inspector, ToolOptions options)
    {
        if (!Valid(inspector, options)) throw new ArgumentException("Invalid network request");
        // All path components are numeric or fixed enums. No caller strings, query input, or game-derived scope.
        return inspector == Inspector.MarketSummary
            ? new(FormattableString.Invariant($"https://universalis.app/api/v2/aggregated/{(options.MarketScope == MarketScope.World ? options.WorldId.ToString(System.Globalization.CultureInfo.InvariantCulture) : options.DataCenter.ToString())}/{options.ItemId}"))
            : new(FormattableString.Invariant($"https://paissadb.zhu.codes/worlds/{options.WorldId}/{(int)options.District}"));
    }
}
