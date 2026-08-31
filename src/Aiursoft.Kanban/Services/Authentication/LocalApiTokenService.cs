using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.Kanban.Services.Authentication;

public sealed record LocalApiToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class LocalApiTokenService(IDataProtectionProvider dataProtectionProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector("Aiursoft.Kanban.LocalApiToken.v1")
        .ToTimeLimitedDataProtector();

    public LocalApiToken Issue(string userId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        return new LocalApiToken(
            LocalApiAuthenticationDefaults.TokenPrefix + _protector.Protect(userId, expiresAt),
            expiresAt);
    }

    public bool TryRead(string accessToken, out string userId)
    {
        userId = string.Empty;
        if (!accessToken.StartsWith(LocalApiAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            userId = _protector.Unprotect(
                accessToken[LocalApiAuthenticationDefaults.TokenPrefix.Length..],
                out _);
            return !string.IsNullOrWhiteSpace(userId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
