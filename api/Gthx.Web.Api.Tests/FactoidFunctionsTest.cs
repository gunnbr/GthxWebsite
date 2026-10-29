using Gthx.Core;
using Gthx.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Gthx.Web.Api.Tests;

public class FactoidFunctionsTest
{
    private FakeWebData _data = null!;
    private MemoryCache _cache = null!;
    private FactoidFunctions _functions = null!;

    [SetUp]
    public void SetUp()
    {
        _data = new FakeWebData();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _functions = new FactoidFunctions(_data, _cache);
    }

    [TearDown]
    public void TearDown() => _cache.Dispose();

    private static HttpRequest Request(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        return context.Request;
    }

    private static T Ok<T>(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        return (T)((OkObjectResult)result).Value!;
    }

    private static void BadRequest(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(((BadRequestObjectResult)result).Value, Is.InstanceOf<ErrorResponse>());
    }

    [Test]
    public async Task Search_TrimsQuery_UsesDefaultLimit_AndMapsResults()
    {
        _data.SearchResults.Add(new Factoid { Id = 7, Item = "hotend", IsAre = false, Value = "hot", User = "nick", Timestamp = new DateTime(2020, 1, 2, 3, 4, 5), IsLocked = true });
        var request = Request("?q=%20hotend%20");

        var response = Ok<SearchResponse>(await _functions.SearchFactoids(request, CancellationToken.None));

        Assert.That(_data.LastSearch, Is.EqualTo(("hotend", FactoidFunctions.DefaultSearchLimit)));
        Assert.That(response.Query, Is.EqualTo("hotend"));
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Timestamp.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(request.HttpContext.Response.Headers.CacheControl.ToString(), Is.EqualTo("public, max-age=60"));
    }

    [Test]
    public async Task Search_CachesCaseInsensitively()
    {
        await _functions.SearchFactoids(Request("?q=Hotend"), CancellationToken.None);
        await _functions.SearchFactoids(Request("?q=hotend"), CancellationToken.None);

        Assert.That(_data.SearchCalls, Is.EqualTo(1));
    }

    [TestCase("?q=x&limit=0")]
    [TestCase("?q=x&limit=51")]
    [TestCase("?q=x&limit=ten")]
    public async Task Search_RejectsBadLimits(string queryString)
    {
        BadRequest(await _functions.SearchFactoids(Request(queryString), CancellationToken.None));
        Assert.That(_data.SearchCalls, Is.Zero);
    }

    [Test]
    public async Task Search_RejectsLongQueries()
    {
        var query = new string('a', FactoidFunctions.MaxQueryLength + 1);

        BadRequest(await _functions.SearchFactoids(Request($"?q={query}"), CancellationToken.None));
    }

    [Test]
    public async Task Top_RanksResults_AndUsesCount()
    {
        _data.TopResults.Add(new FactoidRefCount("pla", 50, [new Factoid { Item = "pla", Value = "a plastic", User = "nick" }]));
        _data.TopResults.Add(new FactoidRefCount("reprap", 20, []));
        var request = Request("?count=2");

        var response = Ok<TopResponse>(await _functions.TopFactoids(request, CancellationToken.None));

        Assert.That(_data.LastTopCount, Is.EqualTo(2));
        Assert.That(response.Factoids.Select(f => (f.Rank, f.Item, f.Count)),
            Is.EqualTo(new[] { (1, "pla", 50), (2, "reprap", 20) }));
        Assert.That(response.Factoids[0].Values[0].Value, Is.EqualTo("a plastic"));
        Assert.That(request.HttpContext.Response.Headers.CacheControl.ToString(), Is.EqualTo("public, max-age=600"));
    }

    [Test]
    public async Task Top_RejectsBadCount()
    {
        BadRequest(await _functions.TopFactoids(Request("?count=100"), CancellationToken.None));
    }

    [Test]
    public async Task Stats_ReturnsCount_AndCaches()
    {
        _data.FactoidCount = 5714;

        var first = Ok<StatsResponse>(await _functions.Stats(Request(""), CancellationToken.None));
        _data.FactoidCount = 1;
        var second = Ok<StatsResponse>(await _functions.Stats(Request(""), CancellationToken.None));

        Assert.That(first.FactoidCount, Is.EqualTo(5714));
        Assert.That(second.FactoidCount, Is.EqualTo(5714));
    }
}
