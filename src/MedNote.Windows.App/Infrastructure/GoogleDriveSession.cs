using System.Diagnostics;
using MedNote.Core;
using MedNote.Infrastructure;

namespace MedNote.Windows.App.Infrastructure;

internal sealed class GoogleDriveSession : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly GoogleOAuthClient _oauth;
    private readonly WindowsCredentialStore _credentialStore = new();
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly CancellationTokenSource _networkCancellation = new();
    private GoogleOAuthCredential? _credential;
    private GoogleOAuthAccessToken? _accessToken;
    private bool _disposed;

    public GoogleDriveSession()
    {
        _oauth = new GoogleOAuthClient(_httpClient);
        try
        {
            _credential = _credentialStore.Read();
        }
        catch (Exception exception)
        {
            CredentialLoadError = exception.Message;
            try
            {
                _credentialStore.Delete();
            }
            catch
            {
                // The original credential error is the useful startup signal.
            }
        }
    }

    public bool IsConnected => _credential is not null;

    public string? ClientId => _credential?.ClientId;

    public string? CredentialLoadError { get; }

    public GoogleDriveAppDataClient CreateDriveClient() => new(_httpClient, GetAccessTokenAsync);

    public async Task ConnectAsync(
        GoogleOAuthClientConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkCancellation.Token);
        var credential = await _oauth.AuthorizeAsync(configuration, OpenBrowser, linked.Token);
        _credentialStore.Write(credential);
        _credential = credential;
        _accessToken = null;
        _ = await GetAccessTokenAsync(linked.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var credential = _credential;
        if (credential is not null)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkCancellation.Token);
            await _oauth.RevokeAsync(credential.RefreshToken, linked.Token);
        }

        _credentialStore.Delete();
        _credential = null;
        _accessToken = null;
    }

    public void CancelNetwork()
    {
        if (!_disposed)
        {
            _networkCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _networkCancellation.Cancel();
        _networkCancellation.Dispose();
        _tokenGate.Dispose();
        _httpClient.Dispose();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkCancellation.Token);
        await _tokenGate.WaitAsync(linked.Token);
        try
        {
            if (_accessToken is { } current && current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return current.Value;
            }

            var credential = _credential
                ?? throw new InvalidOperationException("Google Drive chưa được kết nối.");
            _accessToken = await _oauth.RefreshAsync(credential, linked.Token);
            return _accessToken.Value;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
