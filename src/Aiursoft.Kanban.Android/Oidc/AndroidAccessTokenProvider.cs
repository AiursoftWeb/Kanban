using Aiursoft.Kanban.SDK;

namespace Aiursoft.Kanban.Android.Oidc;

public sealed class AndroidAccessTokenProvider : IKanbanAccessTokenProvider
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly AndroidKeystoreTokenStore _store;
    private OidcPkceClient? _oidc;
    private OidcTokenSet? _tokens;
    private string? _localToken;
    private DateTimeOffset _localTokenExpiresAt;

    public bool IsAuthenticated =>
        _tokens != null ||
        (!string.IsNullOrWhiteSpace(_localToken) && _localTokenExpiresAt > DateTimeOffset.UtcNow);

    public AndroidAccessTokenProvider(AndroidKeystoreTokenStore store)
    {
        _store = store;
    }

    public void SetSession(OidcPkceClient oidc, OidcTokenSet tokens)
    {
        _localToken = null;
        _oidc = oidc;
        _tokens = tokens;
        _store.Save(tokens);
    }

    public void SetLocalSession(string accessToken, DateTimeOffset expiresAt)
    {
        _oidc = null;
        _tokens = null;
        _store.Clear();
        _localToken = accessToken;
        _localTokenExpiresAt = expiresAt;
    }

    public bool TryRestoreSession(OidcPkceClient oidc)
    {
        _localToken = null;
        _localTokenExpiresAt = default;
        var tokens = _store.Load();
        if (tokens == null || !oidc.CanResume(tokens))
        {
            _oidc = null;
            _tokens = null;
            _store.Clear();
            return false;
        }

        _oidc = oidc;
        _tokens = tokens;
        return true;
    }

    public void ClearOidcSession()
    {
        _oidc = null;
        _tokens = null;
        _store.Clear();
    }

    public void Clear()
    {
        _oidc = null;
        _tokens = null;
        _localToken = null;
        _localTokenExpiresAt = default;
        _store.Clear();
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_localToken))
        {
            if (_localTokenExpiresAt > DateTimeOffset.UtcNow)
            {
                return _localToken;
            }
            Clear();
            throw new KanbanAuthenticationRequiredException("Your session expired. Sign in again.");
        }
        if (_tokens == null)
        {
            return null;
        }
        if (_tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _tokens.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_tokens.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                if (string.IsNullOrWhiteSpace(_tokens.RefreshToken))
                {
                    Clear();
                    throw new KanbanAuthenticationRequiredException("Your session expired. Sign in again.");
                }
                _tokens = await (_oidc ?? throw new InvalidOperationException("OIDC client is unavailable."))
                    .RefreshAsync(_tokens, cancellationToken);
                _store.Save(_tokens);
            }
            return _tokens.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}

public sealed class KanbanAuthenticationRequiredException(string message) : InvalidOperationException(message);
