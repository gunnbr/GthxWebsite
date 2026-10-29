using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Gthx.Web.Api;

/// <param name="AllowedObjectIds">Microsoft Entra object IDs of the accounts that may use the admin endpoints.</param>
/// <param name="TrustPrincipalHeaders">
/// Whether the X-MS-CLIENT-PRINCIPAL-* headers can be believed. App Service Authentication ("Easy Auth") sets
/// them from a token it has checked, and removes any the caller sent, but only when it's turned on. Without it
/// anyone could send them, so this is only true when Easy Auth is on, or locally when the
/// Admin:TrustPrincipalHeaders setting says so.
/// </param>
public record AdminOptions(IReadOnlySet<string> AllowedObjectIds, bool TrustPrincipalHeaders);

public record AdminUser(string ObjectId, string Name)
{
    /// <summary>
    /// Who the bot says made a change (FactoidHistory.User, at most 30 characters): "web:brian" for
    /// brian@example.com, so it's clear the change came from the admin panel rather than IRC.
    /// </summary>
    public string HistoryName
    {
        get
        {
            var name = Name.Split('@')[0];
            return "web:" + (name.Length > 25 ? name[..25] : name);
        }
    }
}

public static class AdminAuth
{
    public const string ObjectIdHeader = "X-MS-CLIENT-PRINCIPAL-ID";
    public const string NameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";

    /// <summary>
    /// The signed-in admin, or the response to send instead: 503 when sign-in isn't set up, 401 when nobody is
    /// signed in, 403 for an account that isn't allowed. Also stops anything from caching the response.
    /// </summary>
    public static bool TryGetAdmin(HttpRequest request, AdminOptions options, out AdminUser admin, out IActionResult denied)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        admin = null!;
        denied = null!;

        if (!options.TrustPrincipalHeaders)
        {
            denied = new ObjectResult(new ErrorResponse("Sign-in isn't set up for this API.")) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return false;
        }

        var objectId = request.Headers[ObjectIdHeader].ToString();
        if (string.IsNullOrEmpty(objectId))
        {
            denied = new UnauthorizedObjectResult(new ErrorResponse("Sign in first."));
            return false;
        }

        if (!options.AllowedObjectIds.Contains(objectId))
        {
            denied = new ObjectResult(new ErrorResponse("This account isn't a gthx admin.")) { StatusCode = StatusCodes.Status403Forbidden };
            return false;
        }

        var name = request.Headers[NameHeader].ToString();
        admin = new AdminUser(objectId, string.IsNullOrEmpty(name) ? objectId : name);
        return true;
    }
}
