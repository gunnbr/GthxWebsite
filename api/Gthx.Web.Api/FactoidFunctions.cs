using Gthx.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Caching.Memory;

namespace Gthx.Web.Api;

/// <summary>
/// The website's read-only API. Every response is cached, in this instance and by browsers, so a busy page
/// can't use up the database's 5 DTUs, which it shares with the bot.
/// </summary>
public class FactoidFunctions(IGthxWebData data, IMemoryCache cache)
{
    public const int MaxQueryLength = 100;
    public const int DefaultSearchLimit = 25;
    public const int MaxSearchLimit = 50;
    public const int DefaultTopCount = 10;
    public const int MaxTopCount = 50;

    private static readonly TimeSpan SearchCacheTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TopCacheTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StatsCacheTime = TimeSpan.FromMinutes(10);

    [Function("SearchFactoids")]
    public async Task<IActionResult> SearchFactoids(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "factoids")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Query["q"].ToString().Trim();
        if (query.Length > MaxQueryLength)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The search can be at most {MaxQueryLength} characters."));
        }

        if (!TryReadLimit(request, "limit", DefaultSearchLimit, MaxSearchLimit, out var limit, out var error))
        {
            return error;
        }

        // Case-insensitive key, matching how SQL Server compares the search.
        var key = $"search:{limit}:{query.ToLowerInvariant()}";
        var response = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SearchCacheTime;
            var factoids = await data.SearchFactoidsAsync(query, limit, cancellationToken);
            return new SearchResponse(query, limit, factoids.Select(FactoidValue.From).ToList());
        });

        return Cached(request, response!, SearchCacheTime);
    }

    [Function("TopFactoids")]
    public async Task<IActionResult> TopFactoids(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "factoids/top")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadLimit(request, "count", DefaultTopCount, MaxTopCount, out var count, out var error))
        {
            return error;
        }

        var response = await cache.GetOrCreateAsync($"top:{count}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TopCacheTime;
            var top = await data.GetTopFactoidsAsync(count, cancellationToken);
            return new TopResponse(top
                .Select((t, i) => new TopFactoid(i + 1, t.Item, t.Count, t.Factoids.Select(FactoidValue.From).ToList()))
                .ToList());
        });

        return Cached(request, response!, TopCacheTime);
    }

    [Function("Stats")]
    public async Task<IActionResult> Stats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "stats")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var response = await cache.GetOrCreateAsync("stats", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StatsCacheTime;
            return new StatsResponse(await data.GetFactoidCountAsync(cancellationToken));
        });

        return Cached(request, response!, StatsCacheTime);
    }

    internal static bool TryReadLimit(HttpRequest request, string name, int defaultValue, int max, out int value, out IActionResult error)
    {
        error = null!;
        var text = request.Query[name].ToString();
        if (string.IsNullOrEmpty(text))
        {
            value = defaultValue;
            return true;
        }

        if (int.TryParse(text, out value) && value >= 1 && value <= max)
        {
            return true;
        }

        error = new BadRequestObjectResult(new ErrorResponse($"{name} must be a whole number from 1 to {max}."));
        return false;
    }

    private static OkObjectResult Cached(HttpRequest request, object response, TimeSpan maxAge)
    {
        request.HttpContext.Response.Headers.CacheControl = $"public, max-age={(int)maxAge.TotalSeconds}";
        return new OkObjectResult(response);
    }
}
