using System.Text.Json;
using Gthx.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Gthx.Web.Api;

/// <summary>
/// The admin panel's API. App Service Authentication checks the caller's Microsoft Entra token; these check that
/// the account is an allowed admin. Reads use the public API's read-only database login, and only changes go
/// through <see cref="IGthxAdminData"/>, whose login can write. Nothing here is cached.
/// </summary>
/// <remarks>
/// Routes start with "manage" because the Functions host keeps routes starting with "admin" for itself.
/// </remarks>
public class AdminFunctions(IGthxWebData webData, IGthxAdminData adminData, AdminOptions options)
{
    /// <summary>The longest item the bot will set (GthxNetBot's FactoidModule.MaxFactoidLength).</summary>
    public const int MaxItemLength = 30;
    public const int MaxValueLength = 512;
    public const int DefaultLongLimit = 100;
    public const int MaxLongLimit = 500;
    public const int MaxDeleteCount = 500;

    [Function("AdminMe")]
    public IActionResult Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/me")] HttpRequest request)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out var admin, out var denied))
        {
            return denied;
        }

        return new OkObjectResult(new AdminUserResponse(admin.Name));
    }

    [Function("AdminSearchFactoids")]
    public async Task<IActionResult> SearchFactoids(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/factoids")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out _, out var denied))
        {
            return denied;
        }

        var query = request.Query["q"].ToString().Trim();
        if (query.Length > FactoidFunctions.MaxQueryLength)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The search can be at most {FactoidFunctions.MaxQueryLength} characters."));
        }

        if (!FactoidFunctions.TryReadLimit(request, "limit", FactoidFunctions.MaxSearchLimit, FactoidFunctions.MaxSearchLimit, out var limit, out var error))
        {
            return error;
        }

        var factoids = await webData.SearchFactoidsAsync(query, limit, cancellationToken);
        return new OkObjectResult(new AdminFactoidsResponse(factoids.Select(AdminFactoid.From).ToList()));
    }

    [Function("AdminLongFactoids")]
    public async Task<IActionResult> LongFactoids(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/factoids/long")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out _, out var denied))
        {
            return denied;
        }

        if (!FactoidFunctions.TryReadLimit(request, "limit", DefaultLongLimit, MaxLongLimit, out var limit, out var error))
        {
            return error;
        }

        var factoids = await adminData.GetLongFactoidsAsync(MaxItemLength, limit, cancellationToken);
        return new OkObjectResult(new AdminFactoidsResponse(factoids.Select(AdminFactoid.From).ToList()));
    }

    [Function("AdminUpdateFactoid")]
    public async Task<IActionResult> UpdateFactoid(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/factoids/{id:int}")] HttpRequest request,
        int id,
        CancellationToken cancellationToken)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out var admin, out var denied))
        {
            return denied;
        }

        var body = await ReadBody<UpdateFactoidRequest>(request, cancellationToken);
        var value = body?.Value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxValueLength)
        {
            return new BadRequestObjectResult(new ErrorResponse($"The value must be 1 to {MaxValueLength} characters."));
        }

        var factoid = await adminData.UpdateFactoidValueAsync(id, value, admin.HistoryName, cancellationToken);
        if (factoid == null)
        {
            return new NotFoundObjectResult(new ErrorResponse("That factoid doesn't exist any more."));
        }

        return new OkObjectResult(AdminFactoid.From(factoid));
    }

    [Function("AdminDeleteFactoids")]
    public async Task<IActionResult> DeleteFactoids(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/factoids/delete")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out var admin, out var denied))
        {
            return denied;
        }

        var body = await ReadBody<DeleteFactoidsRequest>(request, cancellationToken);
        var ids = body?.Ids?.Distinct().ToList();
        if (ids == null || ids.Count == 0 || ids.Count > MaxDeleteCount)
        {
            return new BadRequestObjectResult(new ErrorResponse($"Send 1 to {MaxDeleteCount} factoid ids to delete."));
        }

        var deleted = await adminData.DeleteFactoidsAsync(ids, admin.HistoryName, cancellationToken);
        return new OkObjectResult(new DeleteFactoidsResponse(deleted));
    }

    [Function("AdminLockItem")]
    public async Task<IActionResult> LockItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/items/lock")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuth.TryGetAdmin(request, options, out _, out var denied))
        {
            return denied;
        }

        var body = await ReadBody<LockItemRequest>(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(body?.Item))
        {
            return new BadRequestObjectResult(new ErrorResponse("Say which item to lock or unlock."));
        }

        var changed = await adminData.SetItemLockedAsync(body.Item, body.IsLocked, cancellationToken);
        return new OkObjectResult(new LockItemResponse(body.Item, body.IsLocked, changed));
    }

    // Null for a missing or malformed body, which the callers turn into a 400.
    private static async Task<T?> ReadBody<T>(HttpRequest request, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, JsonSerializerOptions.Web, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
