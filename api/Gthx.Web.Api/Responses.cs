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

// The admin panel's shapes. Unlike the public ones, these include Id and IsLocked, since that's what an admin
// changes.

public record AdminFactoid(int Id, string Item, bool IsAre, string Value, string User, DateTime Timestamp, bool IsLocked)
{
    public static AdminFactoid From(Factoid factoid) =>
        new(factoid.Id, factoid.Item, factoid.IsAre, factoid.Value, factoid.User,
            DateTime.SpecifyKind(factoid.Timestamp, DateTimeKind.Utc), factoid.IsLocked);
}

public record AdminUserResponse(string Name);

public record AdminFactoidsResponse(IReadOnlyList<AdminFactoid> Factoids);

public record UpdateFactoidRequest(string? Value);

public record DeleteFactoidsRequest(IReadOnlyList<int>? Ids);

public record DeleteFactoidsResponse(int Deleted);

public record LockItemRequest(string? Item, bool IsLocked);

public record LockItemResponse(string Item, bool IsLocked, int Changed);
