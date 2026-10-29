using System.Text;
using Gthx.Core;
using Gthx.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Gthx.Web.Api.Tests;

public class AdminFunctionsTest
{
    private const string AdminId = "11111111-1111-1111-1111-111111111111";

    private FakeWebData _webData = null!;
    private FakeAdminData _adminData = null!;
    private AdminFunctions _functions = null!;

    [SetUp]
    public void SetUp()
    {
        _webData = new FakeWebData();
        _adminData = new FakeAdminData();
        _functions = Functions(trustHeaders: true);
    }

    private AdminFunctions Functions(bool trustHeaders) =>
        new(_webData, _adminData, new AdminOptions(new HashSet<string>([AdminId], StringComparer.OrdinalIgnoreCase), trustHeaders));

    private static HttpRequest Request(string queryString = "", string? body = null, string? objectId = AdminId, string name = "brian@example.com")
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        if (objectId != null)
        {
            context.Request.Headers[AdminAuth.ObjectIdHeader] = objectId;
            context.Request.Headers[AdminAuth.NameHeader] = name;
        }
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? ""));
        return context.Request;
    }

    private static T Ok<T>(IActionResult result)
    {
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        return (T)((OkObjectResult)result).Value!;
    }

    private static void Status(IActionResult result, int status)
    {
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(status));
        Assert.That(((ObjectResult)result).Value, Is.InstanceOf<ErrorResponse>());
    }

    [Test]
    public void Me_ReturnsName_AndIsNotCached()
    {
        var request = Request();

        var response = Ok<AdminUserResponse>(_functions.Me(request));

        Assert.That(response.Name, Is.EqualTo("brian@example.com"));
        Assert.That(request.HttpContext.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public void Me_RejectsMissingUnknownAndUntrusted()
    {
        Status(_functions.Me(Request(objectId: null)), StatusCodes.Status401Unauthorized);
        Status(_functions.Me(Request(objectId: "22222222-2222-2222-2222-222222222222")), StatusCodes.Status403Forbidden);
        Status(Functions(trustHeaders: false).Me(Request()), StatusCodes.Status503ServiceUnavailable);
    }

    [Test]
    public async Task Writes_RejectUnknownAccounts_WithoutTouchingData()
    {
        var stranger = "22222222-2222-2222-2222-222222222222";

        Status(await _functions.UpdateFactoid(Request(body: "{\"value\":\"x\"}", objectId: stranger), 1, CancellationToken.None), StatusCodes.Status403Forbidden);
        Status(await _functions.DeleteFactoids(Request(body: "{\"ids\":[1]}", objectId: stranger), CancellationToken.None), StatusCodes.Status403Forbidden);
        Status(await _functions.LockItem(Request(body: "{\"item\":\"pla\",\"isLocked\":true}", objectId: stranger), CancellationToken.None), StatusCodes.Status403Forbidden);
        Status(await _functions.EmailKeywords(Request(objectId: stranger), CancellationToken.None), StatusCodes.Status403Forbidden);
        Status(await _functions.AddEmailKeyword(Request(body: "{\"keyword\":\"x\"}", objectId: stranger), CancellationToken.None), StatusCodes.Status403Forbidden);
        Status(await _functions.DeleteEmailKeyword(Request(objectId: stranger), 1, CancellationToken.None), StatusCodes.Status403Forbidden);

        Assert.That(_adminData.Calls, Is.Empty);
    }

    [Test]
    public async Task Search_IncludesIdAndLock()
    {
        _webData.SearchResults.Add(new Factoid { Id = 7, Item = "pla", Value = "a plastic", User = "nick", IsLocked = true });

        var response = Ok<AdminFactoidsResponse>(await _functions.SearchFactoids(Request("?q=pla"), CancellationToken.None));

        Assert.That(_webData.LastSearch, Is.EqualTo(("pla", FactoidFunctions.MaxSearchLimit)));
        Assert.That(response.Factoids.Select(f => (f.Id, f.IsLocked)), Is.EqualTo(new[] { (7, true) }));
    }

    [Test]
    public async Task LongFactoids_UsesTheBotsLimit()
    {
        _adminData.LongResults.Add(new Factoid { Id = 3, Item = new string('x', 40), Value = "oops", User = "nick" });

        var response = Ok<AdminFactoidsResponse>(await _functions.LongFactoids(Request("?limit=10"), CancellationToken.None));

        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "long 30 10" }));
        Assert.That(response.Factoids, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Update_TrimsValue_AndRecordsWhoChangedIt()
    {
        var response = Ok<AdminFactoid>(await _functions.UpdateFactoid(Request(body: "{\"value\":\"  polylactic acid \"}"), 7, CancellationToken.None));

        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "update 7 polylactic acid web:brian" }));
        Assert.That(response.Value, Is.EqualTo("polylactic acid"));
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{\"value\":\"   \"}")]
    [TestCase("{}")]
    public async Task Update_RejectsBadBodies(string body)
    {
        Status(await _functions.UpdateFactoid(Request(body: body), 7, CancellationToken.None), StatusCodes.Status400BadRequest);
        Assert.That(_adminData.Calls, Is.Empty);
    }

    [Test]
    public async Task Update_RejectsLongValues()
    {
        var body = $"{{\"value\":\"{new string('a', AdminFunctions.MaxValueLength + 1)}\"}}";

        Status(await _functions.UpdateFactoid(Request(body: body), 7, CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Update_MissingFactoidIsNotFound()
    {
        Status(await _functions.UpdateFactoid(Request(body: "{\"value\":\"x\"}"), FakeAdminData.MissingId, CancellationToken.None), StatusCodes.Status404NotFound);
    }

    [Test]
    public async Task Delete_SendsDistinctIds()
    {
        var response = Ok<DeleteFactoidsResponse>(await _functions.DeleteFactoids(Request(body: "{\"ids\":[3,4,3]}"), CancellationToken.None));

        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "delete 3,4 web:brian" }));
        Assert.That(response.Deleted, Is.EqualTo(2));
    }

    [TestCase("{\"ids\":[]}")]
    [TestCase("{}")]
    public async Task Delete_RejectsEmpty(string body)
    {
        Status(await _functions.DeleteFactoids(Request(body: body), CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Delete_RejectsTooMany()
    {
        var ids = string.Join(",", Enumerable.Range(1, AdminFunctions.MaxDeleteCount + 1));

        Status(await _functions.DeleteFactoids(Request(body: $"{{\"ids\":[{ids}]}}"), CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Lock_LocksAndUnlocks()
    {
        var locked = Ok<LockItemResponse>(await _functions.LockItem(Request(body: "{\"item\":\"pla\",\"isLocked\":true}"), CancellationToken.None));
        Ok<LockItemResponse>(await _functions.LockItem(Request(body: "{\"item\":\"pla\",\"isLocked\":false}"), CancellationToken.None));

        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "lock pla True", "lock pla False" }));
        Assert.That((locked.Item, locked.IsLocked, locked.Changed), Is.EqualTo(("pla", true, 1)));
    }

    [Test]
    public async Task Lock_RejectsMissingItem()
    {
        Status(await _functions.LockItem(Request(body: "{\"isLocked\":true}"), CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Keywords_Lists()
    {
        _adminData.Keywords.Add(new EmailKeyword { Id = 2, Keyword = "printer", User = "web:brian" });

        var response = Ok<EmailKeywordsResponse>(await _functions.EmailKeywords(Request(), CancellationToken.None));

        Assert.That(response.Keywords.Select(k => (k.Id, k.Keyword)), Is.EqualTo(new[] { (2, "printer") }));
    }

    [Test]
    public async Task Keywords_AddTrims_AndRecordsWhoAddedIt()
    {
        var response = Ok<EmailKeywordResponse>(await _functions.AddEmailKeyword(Request(body: "{\"keyword\":\" printer  \"}"), CancellationToken.None));

        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "add keyword printer web:brian" }));
        Assert.That((response.Keyword, response.User), Is.EqualTo(("printer", "web:brian")));
    }

    [Test]
    public async Task Keywords_AddingAnExistingOneConflicts()
    {
        Status(await _functions.AddEmailKeyword(Request(body: $"{{\"keyword\":\"{FakeAdminData.ExistingKeyword}\"}}"), CancellationToken.None), StatusCodes.Status409Conflict);
    }

    [TestCase("")]
    [TestCase("{}")]
    [TestCase("{\"keyword\":\"  \"}")]
    public async Task Keywords_AddRejectsBadBodies(string body)
    {
        Status(await _functions.AddEmailKeyword(Request(body: body), CancellationToken.None), StatusCodes.Status400BadRequest);
        Assert.That(_adminData.Calls, Is.Empty);
    }

    [Test]
    public async Task Keywords_AddRejectsLongKeywords()
    {
        var body = $"{{\"keyword\":\"{new string('k', AdminFunctions.MaxKeywordLength + 1)}\"}}";

        Status(await _functions.AddEmailKeyword(Request(body: body), CancellationToken.None), StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task Keywords_Delete()
    {
        Assert.That(await _functions.DeleteEmailKeyword(Request(), 2, CancellationToken.None), Is.InstanceOf<NoContentResult>());
        Status(await _functions.DeleteEmailKeyword(Request(), FakeAdminData.MissingId, CancellationToken.None), StatusCodes.Status404NotFound);
        Assert.That(_adminData.Calls, Is.EqualTo(new[] { "delete keyword 2", $"delete keyword {FakeAdminData.MissingId}" }));
    }

    [Test]
    public void HistoryName_FitsTheColumn()
    {
        Assert.That(new AdminUser("id", "brian@example.com").HistoryName, Is.EqualTo("web:brian"));
        Assert.That(new AdminUser("id", new string('n', 60)).HistoryName, Has.Length.EqualTo(29));
    }

    private class FakeAdminData : IGthxAdminData
    {
        public const int MissingId = 404;
        public const string ExistingKeyword = "gthx";

        public List<Factoid> LongResults { get; } = [];
        public List<EmailKeyword> Keywords { get; } = [];
        public List<string> Calls { get; } = [];

        public Task<List<Factoid>> GetLongFactoidsAsync(int maxItemLength, int maxResults, CancellationToken cancellationToken = default)
        {
            Calls.Add($"long {maxItemLength} {maxResults}");
            return Task.FromResult(LongResults.ToList());
        }

        public Task<Factoid> UpdateFactoidValueAsync(int id, string value, string user, CancellationToken cancellationToken = default)
        {
            Calls.Add($"update {id} {value} {user}");
            return Task.FromResult(id == MissingId ? null! : new Factoid { Id = id, Item = "pla", Value = value, User = user });
        }

        public Task<int> DeleteFactoidsAsync(IReadOnlyCollection<int> ids, string user, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete {string.Join(",", ids)} {user}");
            return Task.FromResult(ids.Count);
        }

        public Task<int> SetItemLockedAsync(string item, bool isLocked, CancellationToken cancellationToken = default)
        {
            Calls.Add($"lock {item} {isLocked}");
            return Task.FromResult(1);
        }

        public Task<List<EmailKeyword>> GetEmailKeywordsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Keywords.ToList());
        }

        public Task<EmailKeyword> AddEmailKeywordAsync(string keyword, string user, CancellationToken cancellationToken = default)
        {
            Calls.Add($"add keyword {keyword} {user}");
            return Task.FromResult(keyword == ExistingKeyword ? null! : new EmailKeyword { Id = 1, Keyword = keyword, User = user });
        }

        public Task<bool> DeleteEmailKeywordAsync(int id, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete keyword {id}");
            return Task.FromResult(id != MissingId);
        }
    }
}
