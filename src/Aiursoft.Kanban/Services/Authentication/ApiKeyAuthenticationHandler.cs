using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Aiursoft.Kanban.Entities;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TemplateDbContext db,
    UserManager<User> userManager,
    IUserClaimsPrincipalFactory<User> principalFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TokenPrefix = "kanban_";

    public static string? ReadToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var token = authorization["Bearer ".Length..].Trim();
        return token.StartsWith(TokenPrefix, StringComparison.Ordinal) ? token : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken(Request);
        if (token == null)
        {
            return AuthenticateResult.NoResult();
        }
        if (token.Length != TokenPrefix.Length + 64 || !token[TokenPrefix.Length..].All(char.IsAsciiHexDigit))
        {
            return AuthenticateResult.Fail("Invalid API Key.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        // Credential revocation must not wait for the query cache to expire.
        var key = await db.PersonalApiKeys.AsNoTracking().NotCacheable()
            .SingleOrDefaultAsync(k => k.KeyHash == hash, Context.RequestAborted);
        if (key == null || key.ExpirationTime <= DateTime.UtcNow)
        {
            return AuthenticateResult.Fail("Invalid or expired API Key.");
        }
        var user = await userManager.FindByIdAsync(key.UserId);
        if (user == null || await userManager.IsLockedOutAsync(user))
        {
            return AuthenticateResult.Fail("The API Key owner is unavailable.");
        }

        // Rebuild current user and role claims on every request; keys carry no permission snapshot.
        var principal = await principalFactory.CreateAsync(user);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
