using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Extensions;
using UniqueLights.Api.Services;

namespace UniqueLights.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(IAuthService auth, IOptions<RefreshCookieOptions> cookieOptions) : ApiControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ClientIp, ct);
        SetRefreshCookie(result);
        return CreatedResponse(result.Response, "Account created");
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ClientIp, ct);
        SetRefreshCookie(result);
        return OkResponse(result.Response, "Signed in");
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.RefreshPolicy)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(RefreshCookieOptions.CookieName, out var token);
        try
        {
            var result = await auth.RefreshAsync(token, ClientIp, ct);
            SetRefreshCookie(result);
            return OkResponse(result.Response);
        }
        catch (Exceptions.AppException)
        {
            ClearRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(RefreshCookieOptions.CookieName, out var token);
        await auth.LogoutAsync(token, ct);
        ClearRefreshCookie();
        return OkEmpty("Signed out");
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me(CancellationToken ct) =>
        OkResponse(await auth.GetCurrentUserAsync(CurrentUserId, ct));

    private CookieOptions BuildCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = cookieOptions.Value.Secure,
        SameSite = cookieOptions.Value.SameSite,
        Path = RefreshCookieOptions.CookiePath,
        Expires = expires,
        IsEssential = true
    };

    private void SetRefreshCookie(AuthResult result) =>
        Response.Cookies.Append(RefreshCookieOptions.CookieName, result.RefreshToken,
            BuildCookieOptions(new DateTimeOffset(result.RefreshTokenExpiresAt, TimeSpan.Zero)));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieOptions.CookieName, BuildCookieOptions(null));
}
