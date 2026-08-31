using Aiursoft.Kanban.SDK;

namespace Aiursoft.Kanban.Android.Oidc;

public sealed class AndroidAccessTokenProvider : IKanbanAccessTokenProvider
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private OidcPkceClient? _oidc;
    private OidcTokenSet? _tokens;
    private string? _localToken;
    private DateTimeOffset _localTokenExpiresAt;

    public bool IsAuthenticated =>
        _tokens != null ||
        (!string.IsNullOrWhiteSpace(_localToken) && _localTokenExpiresAt > DateTimeOffset.UtcNow);

    public void SetSession(OidcPkceClient oidc, OidcTokenSet tokens)
    {
        _localToken = null;
        _oidc = oidc;
        _tokens = tokens;
    }

    public void SetLocalSession(string accessToken, DateTimeOffset expiresAt)
    {
        _oidc = null;
        _tokens = null;
        _localToken = accessToken;
        _localTokenExpiresAt = expiresAt;
    }

    public void Clear()
    {
        _oidc = null;
        _tokens = null;
        _localToken = null;
        _localTokenExpiresAt = default;
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
