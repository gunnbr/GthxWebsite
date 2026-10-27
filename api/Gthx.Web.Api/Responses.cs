using Gthx.Core;

namespace Gthx.Web.Api;

// The JSON shapes the website reads. Kept separate from the EF entities so the API doesn't leak columns
// (such as IsLocked or Id) and doesn't change shape when the database does.

public record FactoidValue(string Item, bool IsAre, string Value, string User, DateTime Timestamp)
{
    public static FactoidValue From(Factoid factoid) =>
        new(factoid.Item, factoid.IsAre, factoid.Value, factoid.User,
            // The bot stores UTC, but SQL Server's datetime2 comes back with an unspecified kind.
            DateTime.SpecifyKind(factoid.Timestamp, DateTimeKind.Utc));
}

public record SearchResponse(string Query, int Limit, IReadOnlyList<FactoidValue> Results);

public record TopFactoid(int Rank, string Item, int Count, IReadOnlyList<FactoidValue> Values);

public record TopResponse(IReadOnlyList<TopFactoid> Factoids);

public record StatsResponse(int FactoidCount);

public record ErrorResponse(string Error);
