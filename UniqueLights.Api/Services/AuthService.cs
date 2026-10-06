using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string? refreshToken, string? ipAddress, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct);
    Task RevokeAllAsync(Guid userId, CancellationToken ct);
}

public sealed class AuthService(
    AppDbContext db,
    IJwtTokenService jwt,
    IRefreshTokenService refreshTokens,
    IPasswordHashingService passwords,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password";
    private const string Deactivated = "Your account has been deactivated";
    private const string InvalidSession = "Your session has expired. Please sign in again.";

    /// <summary>A rotated token re-presented within this window is treated as a benign concurrent refresh (e.g. two tabs).</summary>
    private static readonly TimeSpan ReuseGraceWindow = TimeSpan.FromSeconds(20);

    // Used to keep login timing similar whether or not the email exists.
    private static readonly User TimingUser = new() { Email = "timing@unique-lights.invalid" };
    private static string? _timingHash;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, string? ipAddress, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
        {
            throw new ConflictException("An account with this email already exists");
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            IsActive = true,
            LastLoginAt = Now
        };
        user.PasswordHash = passwords.Hash(user, request.Password);
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = Roles.CustomerId });

        db.Users.Add(user);
        var result = await IssueTokensAsync(user, [Roles.Customer], ipAddress, ct);
        logger.LogInformation("User {UserId} registered", user.Id);
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            _timingHash ??= passwords.Hash(TimingUser, Guid.NewGuid().ToString());
            passwords.Verify(new User { PasswordHash = _timingHash }, request.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var (valid, needsRehash) = passwords.Verify(user, request.Password);
        if (!valid)
        {
            logger.LogInformation("Failed login for user {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException(Deactivated);
        }

        if (needsRehash) user.PasswordHash = passwords.Hash(user, request.Password);
        user.LastLoginAt = Now;

        // Housekeeping: drop this user's long-expired tokens.
        var cutoff = Now.AddDays(-1);
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);

        var roles = await GetRolesAsync(user.Id, ct);
        return await IssueTokensAsync(user, roles, ipAddress, ct);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, string? ipAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedException(InvalidSession);

        var hash = refreshTokens.Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) throw new UnauthorizedException(InvalidSession);

        var now = Now;
        if (token.RevokedAt.HasValue)
        {
            var benignRace = token.ReplacedByTokenHash is not null && now - token.RevokedAt.Value <= ReuseGraceWindow;
            if (!benignRace)
            {
                logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", token.UserId);
                await RevokeAllAsync(token.UserId, ct);
                throw new UnauthorizedException(InvalidSession);
            }
        }
        else if (now >= token.ExpiresAt)
        {
            throw new UnauthorizedException(InvalidSession);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null) throw new UnauthorizedException(InvalidSession);
        if (!user.IsActive)
        {
            await RevokeAllAsync(user.Id, ct);
            throw new ForbiddenException(Deactivated);
        }

        var roles = await GetRolesAsync(user.Id, ct);
        return await IssueTokensAsync(user, roles, ipAddress, ct, rotated: token.RevokedAt.HasValue ? null : token);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = refreshTokens.Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt.HasValue) return;
        token.RevokedAt = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new NotFoundException("User not found");
        var roles = await GetRolesAsync(userId, ct);
        return user.ToDto(roles);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.Role.Name).OrderBy(n => n).ToListAsync(ct);

    public Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = Now;
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, IReadOnlyList<string> roles, string? ipAddress,
        CancellationToken ct, RefreshToken? rotated = null)
    {
        var now = Now;
        var (raw, hash) = refreshTokens.Generate();
        var expires = now.AddDays(jwtOptions.Value.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = expires,
            CreatedByIp = Truncate(ipAddress, 64)
        });

        if (rotated is not null)
        {
            rotated.RevokedAt = now;
            rotated.ReplacedByTokenHash = hash;
        }

        await db.SaveChangesAsync(ct);

        var (accessToken, accessExpires) = jwt.CreateAccessToken(user, roles);
        return new AuthResult(
            new AuthResponse { AccessToken = accessToken, ExpiresAt = accessExpires, User = user.ToDto(roles) },
            raw,
            expires);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
