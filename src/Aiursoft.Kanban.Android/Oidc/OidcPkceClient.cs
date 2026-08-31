using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.Content;

namespace Aiursoft.Kanban.Android.Oidc;

public sealed class OidcPkceClient(HttpClient http, ISharedPreferences preferences)
{
    private const string StateKey = "oidc.state";
    private const string VerifierKey = "oidc.verifier";
    private const string TokenEndpointKey = "oidc.token_endpoint";
    private const string ClientIdKey = "oidc.client_id";
    private const string RedirectUriKey = "oidc.redirect_uri";

    private string _authorizationEndpoint = string.Empty;
    private string _tokenEndpoint = string.Empty;
    private string _clientId = string.Empty;
    private string _redirectUri = string.Empty;
    private IReadOnlyList<string> _scopes = [];

    public async Task ConfigureAsync(
        string authority,
        string clientId,
        string redirectUri,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var discoveryUri = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
        using var response = await http.GetAsync(discoveryUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        _authorizationEndpoint = document.RootElement.GetProperty("authorization_endpoint").GetString()
            ?? throw new InvalidOperationException("OIDC discovery has no authorization_endpoint.");
        _tokenEndpoint = document.RootElement.GetProperty("token_endpoint").GetString()
            ?? throw new InvalidOperationException("OIDC discovery has no token_endpoint.");
        _clientId = clientId;
        _redirectUri = redirectUri;
        _scopes = scopes;
    }

    public Uri CreateAuthorizationUri()
    {
        EnsureConfigured();
        var state = RandomUrlSafe(32);
        var verifier = RandomUrlSafe(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        preferences.Edit()!
            .PutString(StateKey, state)!
            .PutString(VerifierKey, verifier)!
            .PutString(TokenEndpointKey, _tokenEndpoint)!
            .PutString(ClientIdKey, _clientId)!
            .PutString(RedirectUriKey, _redirectUri)!
            .Apply();

        var values = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', _scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        return new Uri($"{_authorizationEndpoint}?{ToQuery(values)}");
    }

    public async Task<OidcTokenSet> CompleteAuthorizationAsync(
        Uri callback,
        CancellationToken cancellationToken = default)
    {
        var query = ParseQuery(callback.Query);
        if (query.TryGetValue("error", out var error))
        {
            throw new InvalidOperationException($"OIDC authorization failed: {error}");
        }
        if (!query.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(preferences.GetString(StateKey, string.Empty) ?? string.Empty)))
        {
            throw new InvalidOperationException("OIDC state validation failed.");
        }
        if (!query.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException("OIDC callback has no authorization code.");
        }

        var tokenEndpoint = preferences.GetString(TokenEndpointKey, string.Empty) ?? string.Empty;
        var clientId = preferences.GetString(ClientIdKey, string.Empty) ?? string.Empty;
        var redirectUri = preferences.GetString(RedirectUriKey, string.Empty) ?? string.Empty;
        var verifier = preferences.GetString(VerifierKey, string.Empty) ?? string.Empty;
        var tokenSet = await RequestTokensAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier
        }, cancellationToken);
        preferences.Edit()!.Remove(StateKey)!.Remove(VerifierKey)!.Apply();
        return tokenSet with { TokenEndpoint = tokenEndpoint, ClientId = clientId };
    }

    public Task<OidcTokenSet> RefreshAsync(OidcTokenSet current, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(current.RefreshToken))
        {
            throw new InvalidOperationException("The OIDC provider did not issue a refresh token.");
        }
        return RequestTokensAsync(current.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = current.ClientId,
            ["refresh_token"] = current.RefreshToken
        }, cancellationToken, current.RefreshToken);
    }

    private async Task<OidcTokenSet> RequestTokensAsync(
        string tokenEndpoint,
        Dictionary<string, string> form,
        CancellationToken cancellationToken,
        string? existingRefreshToken = null)
    {
        using var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OIDC token endpoint returned {(int)response.StatusCode}: {json}");
        }
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("OIDC token response has no access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 300;
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : existingRefreshToken;
        return new OidcTokenSet(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            tokenEndpoint,
            form["client_id"]);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_authorizationEndpoint) ||
            string.IsNullOrWhiteSpace(_tokenEndpoint) ||
            string.IsNullOrWhiteSpace(_clientId) ||
            string.IsNullOrWhiteSpace(_redirectUri))
        {
            throw new InvalidOperationException("OIDC has not been configured from the Kanban server.");
        }
    }

    private static string RandomUrlSafe(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Base64Url(bytes);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ToQuery(IEnumerable<KeyValuePair<string, string>> values) => string.Join('&',
        values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static Dictionary<string, string> ParseQuery(string query) => query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            part => Uri.UnescapeDataString(part[0]),
            part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty);
}

public sealed record OidcTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string TokenEndpoint,
    string ClientId);
