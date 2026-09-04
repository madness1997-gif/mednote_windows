using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedNote.Core;

namespace MedNote.Infrastructure;

public sealed class GoogleOAuthClient(HttpClient httpClient)
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly JsonSerializerOptions _json = JsonDefaults.Create();

    public async Task<GoogleOAuthCredential> AuthorizeAsync(
        GoogleOAuthClientConfiguration configuration,
        Action<Uri> openBrowser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(openBrowser);
        GoogleDriveOAuth.ValidateClientId(configuration.ClientId);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = GoogleDriveOAuth.CreateAuthorizationRequest(
            configuration.ClientId,
            new Uri($"http://127.0.0.1:{port}/oauth2/callback"));
        openBrowser(request.AuthorizationUri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        var callback = await ReadCallbackAsync(client, timeout.Token);
        if (!callback.Query.TryGetValue("state", out var state)
            || !CryptographicEquals(state, request.State))
        {
            await RespondAsync(client, HttpStatusCode.BadRequest, "Yêu cầu OAuth không hợp lệ.", timeout.Token);
            throw new InvalidDataException("OAuth state không khớp.");
        }

        if (callback.Query.TryGetValue("error", out var oauthError))
        {
            await RespondAsync(client, HttpStatusCode.BadRequest, "Đăng nhập đã bị hủy.", timeout.Token);
            throw new InvalidOperationException($"Google OAuth từ chối yêu cầu: {oauthError}");
        }

        if (!callback.Query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            await RespondAsync(client, HttpStatusCode.BadRequest, "Không nhận được mã đăng nhập.", timeout.Token);
            throw new InvalidDataException("Google OAuth không trả về authorization code.");
        }

        TokenPayload token;
        try
        {
            token = await PostTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId.Trim(),
                ["client_secret"] = configuration.ClientSecret.Trim(),
                ["code"] = code,
                ["code_verifier"] = request.CodeVerifier,
                ["redirect_uri"] = request.RedirectUri.AbsoluteUri,
                ["grant_type"] = "authorization_code",
            }, cancellationToken);
        }
        catch
        {
            await RespondAsync(client, HttpStatusCode.BadRequest, "Không hoàn tất được kết nối Google Drive.", CancellationToken.None);
            throw;
        }

        await RespondAsync(client, HttpStatusCode.OK, "Đã kết nối MedNote. Bạn có thể đóng tab này.", timeout.Token);
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidDataException("Google không trả về refresh token; hãy thu hồi quyền cũ rồi kết nối lại.");
        }

        return new GoogleOAuthCredential
        {
            ClientId = configuration.ClientId.Trim(),
            ClientSecret = configuration.ClientSecret.Trim(),
            RefreshToken = token.RefreshToken,
        };
    }

    public async Task<GoogleOAuthAccessToken> RefreshAsync(
        GoogleOAuthCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        GoogleDriveOAuth.ValidateClientId(credential.ClientId);
        if (string.IsNullOrWhiteSpace(credential.RefreshToken))
        {
            throw new InvalidOperationException("Chưa có refresh token Google Drive.");
        }

        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = credential.ClientId,
            ["client_secret"] = credential.ClientSecret,
            ["refresh_token"] = credential.RefreshToken,
            ["grant_type"] = "refresh_token",
        }, cancellationToken);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidDataException("Google không trả về access token.");
        }

        return new GoogleOAuthAccessToken
        {
            Value = token.AccessToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn)),
        };
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        using var response = await _httpClient.PostAsync(
            RevokeEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google không thu hồi token (HTTP {(int)response.StatusCode}).");
        }
    }

    private async Task<TokenPayload> PostTokenAsync(
        Dictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(values.GetValueOrDefault("client_secret")))
        {
            values.Remove("client_secret");
        }

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(values), cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(_json, cancellationToken)
            ?? throw new InvalidDataException("Google OAuth trả về phản hồi rỗng.");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(payload.ErrorDescription ?? payload.Error ?? $"Google OAuth lỗi {(int)response.StatusCode}.");
        }

        return payload;
    }

    private static async Task<OAuthCallback> ReadCallbackAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > 8192)
        {
            throw new InvalidDataException("HTTP callback OAuth không hợp lệ.");
        }

        var parts = requestLine.Split(' ', 3);
        if (parts.Length != 3 || parts[0] != "GET" || !Uri.TryCreate($"http://127.0.0.1{parts[1]}", UriKind.Absolute, out var uri))
        {
            throw new InvalidDataException("HTTP callback OAuth không hợp lệ.");
        }

        if (uri.AbsolutePath != "/oauth2/callback")
        {
            throw new InvalidDataException("Đường dẫn callback OAuth không hợp lệ.");
        }

        var headerBytes = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            headerBytes += line.Length;
            if (headerBytes > 16 * 1024)
            {
                throw new InvalidDataException("HTTP callback OAuth vượt giới hạn header.");
            }
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0].Replace('+', ' ')),
                item => Uri.UnescapeDataString((item.Length > 1 ? item[1] : string.Empty).Replace('+', ' ')),
                StringComparer.Ordinal);
        return new OAuthCallback(query);
    }

    private static async Task RespondAsync(
        TcpClient client,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>MedNote</title><p>{WebUtility.HtmlEncode(message)}</p>");
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        var stream = client.GetStream();
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record OAuthCallback(Dictionary<string, string> Query);

    private sealed record TokenPayload
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}
