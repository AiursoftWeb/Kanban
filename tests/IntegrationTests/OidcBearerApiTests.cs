using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aiursoft.DbTools;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
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
    public async Task PersonalKeyAuthenticatesInOidcModeWithoutChangingMobileLogin()
    {
        using var reservation = new TestPortReservation();
        var api = await AppAsync<Startup>(
        [
            "--AppSettings:AuthProvider=OIDC",
            "--AppSettings:OIDC:Authority=https://unavailable.invalid",
            "--AppSettings:OIDC:MobileAuthority=https://unavailable.invalid",
            "--AppSettings:OIDC:ClientId=test-web",
            "--AppSettings:OIDC:ClientSecret=test-secret"
        ], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(api);
            await api.UpdateDbAsync<TemplateDbContext>();
            await api.StartAsync();
            const string rawKey = "kanban_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
            await using (var scope = api.Services.CreateAsyncScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                var user = new User { UserName = "key-user", Email = "key@example.com", DisplayName = "Key User" };
                Assert.IsTrue((await manager.CreateAsync(user)).Succeeded);
                var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
                db.PersonalApiKeys.Add(new PersonalApiKey
                {
                    Name = "Automation", UserId = user.Id, Prefix = rawKey[..15],
                    KeyHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey))),
                    ExpirationTime = DateTime.UtcNow.AddDays(1)
                });
                await db.SaveChangesAsync();
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{reservation.Port}/api/v1/boards");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var localLogin = await Http.PostAsJsonAsync($"http://127.0.0.1:{reservation.Port}/api/v1/auth/local/login",
                new LocalLoginRequest { EmailOrUserName = "key-user", Password = "not-a-local-password" });
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, localLogin.StatusCode);
        }
        finally
        {
            await api.StopAsync();
            await api.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task HostedMobileOidcConfigurationWorksWithoutDeploymentOverrides()
    {
        using var apiReservation = new TestPortReservation();
        var apiPort = apiReservation.Port;
        var api = await AppAsync<Startup>(
        [
            "--AppSettings:AuthProvider=OIDC",
            "--AppSettings:OIDC:Authority=https://auth.aiursoft.com/application/o/kanban",
            "--AppSettings:OIDC:ClientId=test-web",
            "--AppSettings:OIDC:ClientSecret=test-secret"
        ], port: apiPort);
        try
        {
            apiReservation.ConfigureServer(api);
            await api.UpdateDbAsync<TemplateDbContext>();
            await api.StartAsync();

            var configuration = await Http.GetFromJsonAsync<MobileConfigurationResponse>(
                $"http://127.0.0.1:{apiPort}/api/v1/config");

            Assert.IsNotNull(configuration);
            Assert.AreEqual(
                "https://auth.aiursoft.com/application/o/kanban-android",
                configuration.Authority);
            Assert.AreEqual("kanban-android", configuration.ClientId);
            Assert.AreEqual("com.aiursoft.kanban:/oauth2redirect", configuration.RedirectUri);
            CollectionAssert.AreEqual(
                new[] { "openid", "profile", "email", "offline_access" },
                configuration.Scopes);
        }
        finally
        {
            await api.StopAsync();
            await api.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ValidOidcAccessTokenAuthenticatesAndLinksLocalUser()
    {
        using var identityReservation = new TestPortReservation();
        var identityPort = identityReservation.Port;
        using var apiReservation = new TestPortReservation();
        var apiPort = apiReservation.Port;
        var authority = $"http://127.0.0.1:{identityPort}";
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
        var identityProvider = BuildIdentityProvider(identityReservation, authority, signingKey, rsa);
        IHost? api = null;
        try
        {
            await identityProvider.StartAsync();
            api = await AppAsync<Startup>(
            [
                "--AppSettings:AuthProvider=OIDC",
                "--AppSettings:OIDC:Authority=http://web-provider.invalid",
                $"--AppSettings:OIDC:MobileAuthority={authority}",
                "--AppSettings:OIDC:RequireHttpsMetadata=false",
                "--AppSettings:OIDC:ClientId=test-web",
                "--AppSettings:OIDC:ClientSecret=test-secret",
                "--AppSettings:OIDC:MobileClientId=test-android",
                "--AppSettings:OIDC:ApiAudience=test-android",
                "--AppSettings:OIDC:ApiScope="
            ], port: apiPort);
            apiReservation.ConfigureServer(api);
            await api.UpdateDbAsync<TemplateDbContext>();
            await api.StartAsync();

            var configuration = await Http.GetFromJsonAsync<MobileConfigurationResponse>(
                $"http://127.0.0.1:{apiPort}/api/v1/config");
            Assert.IsNotNull(configuration);
            Assert.AreEqual(authority, configuration.Authority);
            Assert.AreEqual("test-android", configuration.ClientId);
            CollectionAssert.AreEqual(
                new[] { "openid", "profile", "email", "offline_access" },
                configuration.Scopes);

            var subject = $"mobile-{Guid.NewGuid():N}";
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: authority,
                audience: "test-android",
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
        TestPortReservation reservation,
        string authority,
        RsaSecurityKey signingKey,
        RSA rsa)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{reservation.Port}");
        var app = builder.Build();
        reservation.ConfigureServer(app);
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
