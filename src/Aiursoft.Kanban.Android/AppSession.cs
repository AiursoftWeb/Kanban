using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Android.Content;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Kanban.Android;

public sealed class AppSession : IDisposable
{
    private const string ServerKey = "kanban.server";
    private const string SelectedBoardKey = "kanban.selected_board";
    public const string DefaultServer = "https://192.168.50.146:5443";

    private readonly ISharedPreferences _preferences;
    private readonly AndroidAccessTokenProvider _tokens = new();
    private readonly HttpClient _oidcHttp;
    private readonly byte[] _pinnedCertificateHash;
    private ServiceProvider? _services;
    private OidcPkceClient? _oidc;

    public AppSession(Context context)
    {
        _preferences = context.GetSharedPreferences("kanban", FileCreationMode.Private)!;
        _pinnedCertificateHash = LoadPinnedCertificateHash(context);
        _oidcHttp = new HttpClient(CreateHttpHandler());
    }

    public string Endpoint => _preferences.GetString(ServerKey, DefaultServer) ?? DefaultServer;
    public int SelectedBoardId
    {
        get => _preferences.GetInt(SelectedBoardKey, 0);
        set => _preferences.Edit()!.PutInt(SelectedBoardKey, value)!.Apply();
    }
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsAuthenticated => _tokens.IsAuthenticated;
    public MobileConfigurationResponse? Configuration { get; private set; }
    public KanbanApiClient? Api { get; private set; }

    public async Task<MobileConfigurationResponse> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        endpoint = NormalizeEndpoint(endpoint);
        if (!string.Equals(endpoint, Endpoint, StringComparison.OrdinalIgnoreCase))
        {
            SignOut();
        }
        _preferences.Edit()!.PutString(ServerKey, endpoint)!.Apply();
        _services?.Dispose();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IKanbanAccessTokenProvider>(_tokens);
        services.AddKanbanSdk(endpoint);
        services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(CreateHttpHandler);
        _services = services.BuildServiceProvider();
        Api = _services.GetRequiredService<KanbanApiClient>();
        Configuration = await Api.GetConfigurationAsync();

        if (string.Equals(Configuration.AuthenticationMode, "OIDC", StringComparison.OrdinalIgnoreCase))
        {
            _oidc = new OidcPkceClient(_oidcHttp, _preferences);
            await _oidc.ConfigureAsync(
                Configuration.Authority,
                Configuration.ClientId,
                Configuration.RedirectUri,
                Configuration.Scopes,
                cancellationToken);
        }
        else if (string.Equals(Configuration.AuthenticationMode, "Local", StringComparison.OrdinalIgnoreCase))
        {
            _oidc = null;
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported authentication mode: {Configuration.AuthenticationMode}.");
        }

        return Configuration;
    }

    public Uri CreateAuthorizationUri() =>
        (_oidc ?? throw new InvalidOperationException("Connect to an OIDC server first."))
        .CreateAuthorizationUri();

    public async Task CompleteAuthorizationAsync(
        Uri callback,
        CancellationToken cancellationToken = default)
    {
        if (_oidc == null || Api == null)
        {
            await ConnectAsync(Endpoint, cancellationToken);
        }
        var tokens = await (_oidc ?? throw new InvalidOperationException("OIDC is not configured."))
            .CompleteAuthorizationAsync(callback, cancellationToken);
        _tokens.SetSession(_oidc, tokens);
        DisplayName = "Signed in";
    }

    public async Task LoginLocalAsync(
        string identity,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await RequireApi().LoginLocalAsync(new LocalLoginRequest
        {
            EmailOrUserName = identity,
            Password = password
        });
        _tokens.SetLocalSession(response.AccessToken, response.ExpiresAt);
        DisplayName = response.DisplayName;
    }

    public async Task RegisterLocalAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await RequireApi().RegisterLocalAsync(new LocalRegistrationRequest
        {
            Email = email,
            Password = password
        });
        _tokens.SetLocalSession(response.AccessToken, response.ExpiresAt);
        DisplayName = response.DisplayName;
    }

    public void SignOut()
    {
        _tokens.Clear();
        DisplayName = string.Empty;
        SelectedBoardId = 0;
    }

    public KanbanApiClient RequireApi() =>
        Api ?? throw new InvalidOperationException("Connect to a server first.");

    public void Dispose()
    {
        _services?.Dispose();
        _oidcHttp.Dispose();
    }

    private SocketsHttpHandler CreateHttpHandler() => new()
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = ValidateServerCertificate
        }
    };

    private bool ValidateServerCertificate(
        object _,
        X509Certificate? certificate,
        X509Chain? _chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }
        return certificate != null && CryptographicOperations.FixedTimeEquals(
            certificate.GetCertHash(HashAlgorithmName.SHA256),
            _pinnedCertificateHash);
    }

    private static byte[] LoadPinnedCertificateHash(Context context)
    {
        using var source = context.Resources!.OpenRawResource(Resource.Raw.kanban_server_leaf);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        using var certificate = X509CertificateLoader.LoadCertificate(buffer.ToArray());
        return certificate.GetCertHash(HashAlgorithmName.SHA256);
    }

    private static string NormalizeEndpoint(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Enter an absolute HTTP or HTTPS server URL.");
        }
        return uri.ToString().TrimEnd('/');
    }
}
