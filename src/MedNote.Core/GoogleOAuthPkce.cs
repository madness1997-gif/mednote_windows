using System.Security.Cryptography;
using System.Text;

namespace MedNote.Core;

public static class GoogleDriveOAuth
{
    public const string AppDataScope = "https://www.googleapis.com/auth/drive.appdata";

    public static GoogleOAuthAuthorizationRequest CreateAuthorizationRequest(
        string clientId,
        Uri redirectUri,
        string? state = null,
        string? verifier = null)
    {
        ValidateClientId(clientId);
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("OAuth Desktop phải dùng loopback HTTP.", nameof(redirectUri));
        }

        state ??= Base64Url(RandomNumberGenerator.GetBytes(32));
        verifier ??= Base64Url(RandomNumberGenerator.GetBytes(64));
        if (verifier.Length is < 43 or > 128)
        {
            throw new ArgumentException("PKCE verifier phải dài 43–128 ký tự.", nameof(verifier));
        }

        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = AppDataScope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };
        var uri = new UriBuilder("https://accounts.google.com/o/oauth2/v2/auth")
        {
            Query = string.Join("&", query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}")),
        }.Uri;
        return new GoogleOAuthAuthorizationRequest
        {
            AuthorizationUri = uri,
            RedirectUri = redirectUri,
            State = state,
            CodeVerifier = verifier,
            CodeChallenge = challenge,
        };
    }

    public static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)
            || !clientId.Trim().EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
        {
            throw new ArgumentException("OAuth Client ID Desktop không hợp lệ.", nameof(clientId));
        }
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record GoogleOAuthAuthorizationRequest
{
    public Uri AuthorizationUri { get; init; } = new("https://accounts.google.com");

    public Uri RedirectUri { get; init; } = new("http://127.0.0.1");

    public string State { get; init; } = string.Empty;

    public string CodeVerifier { get; init; } = string.Empty;

    public string CodeChallenge { get; init; } = string.Empty;
}

public sealed record GoogleOAuthClientConfiguration
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}

public sealed record GoogleOAuthCredential
{
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record GoogleOAuthAccessToken
{
    public string Value { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; init; }
}
