using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class OidcBearerApiTests
{
    private static readonly HttpClient Http = new();

    [TestMethod]
    public async Task ValidOidcAccessTokenAuthenticatesAndLinksLocalUser()
    {
        var identityPort = Network.GetAvailablePort();
        var apiPort = Network.GetAvailablePort();
        var authority = $"http://127.0.0.1:{identityPort}";
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
        var identityProvider = BuildIdentityProvider(identityPort, authority, signingKey, rsa);
        IHost? api = null;
        try
        {
            await identityProvider.StartAsync();
            api = await AppAsync<Startup>(
            [
                "--AppSettings:AuthProvider=OIDC",
                $"--AppSettings:OIDC:Authority={authority}",
                "--AppSettings:OIDC:RequireHttpsMetadata=false",
                "--AppSettings:OIDC:ClientId=test-web",
                "--AppSettings:OIDC:ClientSecret=test-secret",
                "--AppSettings:OIDC:MobileClientId=test-android",
                "--AppSettings:OIDC:ApiAudience=kanban-api",
                "--AppSettings:OIDC:ApiScope=kanban-api"
            ], port: apiPort);
            await api.UpdateDbAsync<TemplateDbContext>();
            await api.StartAsync();

            var subject = $"mobile-{Guid.NewGuid():N}";
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: authority,
                audience: "kanban-api",
                claims:
                [
                    new Claim("sub", subject),
                    new Claim("preferred_username", "mobile-user"),
                    new Claim("name", "Mobile User"),
                    new Claim("email", $"{subject}@example.com")
                ],
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{apiPort}/api/v1/boards");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await using var scope = api.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var localUser = await userManager.FindByLoginAsync("OpenIdConnect", subject);
            Assert.IsNotNull(localUser);
            Assert.AreEqual("Mobile User", localUser.DisplayName);
        }
        finally
        {
            if (api != null)
            {
                await api.StopAsync();
                api.Dispose();
            }
            await identityProvider.StopAsync();
            await identityProvider.DisposeAsync();
        }
    }

    private static WebApplication BuildIdentityProvider(
        int port,
        string authority,
        RsaSecurityKey signingKey,
        RSA rsa)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        app.MapGet("/.well-known/openid-configuration", () => new
        {
            issuer = authority,
            authorization_endpoint = $"{authority}/authorize",
            token_endpoint = $"{authority}/token",
            jwks_uri = $"{authority}/jwks",
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" }
        });
        app.MapGet("/jwks", () =>
        {
            var parameters = rsa.ExportParameters(false);
            return new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = signingKey.KeyId,
                        alg = "RS256",
                        n = Base64UrlEncoder.Encode(parameters.Modulus),
                        e = Base64UrlEncoder.Encode(parameters.Exponent)
                    }
                }
            };
        });
        return app;
    }
}
