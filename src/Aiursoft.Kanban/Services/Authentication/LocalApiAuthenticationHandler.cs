using System.Text.Encodings.Web;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Authentication;

public sealed class LocalApiAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalApiTokenService tokenService,
    UserManager<User> userManager,
    IUserClaimsPrincipalFactory<User> principalFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (!token.StartsWith(LocalApiAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }
        if (!tokenService.TryRead(token, out var userId))
        {
            return AuthenticateResult.Fail("The local API token is invalid or expired.");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return AuthenticateResult.Fail("The local API user no longer exists.");
        }

        var principal = await principalFactory.CreateAsync(user);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
