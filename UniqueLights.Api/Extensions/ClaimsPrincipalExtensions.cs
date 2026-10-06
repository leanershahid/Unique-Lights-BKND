using System.Security.Claims;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Exceptions;

namespace UniqueLights.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Returns the authenticated user's id (the "sub" claim) or throws 401.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId() ?? throw new UnauthorizedException();

    public static Guid? TryGetUserId(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        var value = principal.FindFirstValue(AppClaimTypes.Subject);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(Roles.Admin);
}
