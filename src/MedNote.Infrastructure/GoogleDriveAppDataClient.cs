using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MedNote.Core;

namespace MedNote.Infrastructure;

public sealed class GoogleDriveAppDataClient : IDriveAppDataClient
{
    private const string ApiRoot = "https://www.googleapis.com/drive/v3";
    private const string UploadRoot = "https://www.googleapis.com/upload/drive/v3";
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create();

    public GoogleDriveAppDataClient(HttpClient httpClient, Func<CancellationToken, Task<string>> accessToken)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    }

    public async Task<DriveRemoteFile?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var escapedName = name.Replace("'", "\\'", StringComparison.Ordinal);
        var query = Uri.EscapeDataString($"name = '{escapedName}' and 'appDataFolder' in parents and trashed = false");
        var fields = Uri.EscapeDataString("files(id,name,version,modifiedTime)");
        using var response = await SendAsync(
            HttpMethod.Get,
            $"{ApiRoot}/files?spaces=appDataFolder&pageSize=100&orderBy=modifiedTime%20desc&q={query}&fields={fields}",
            content: null,
            expectedETag: null,
            cancellationToken);
        var list = await response.Content.ReadFromJsonAsync<DriveFileList>(_json, cancellationToken)
            ?? throw new InvalidDataException("Google Drive trả về danh sách rỗng.");
        var file = list.Files.FirstOrDefault();
        return file is null ? null : await GetMetadataAsync(file.Id, cancellationToken);
    }

    public async Task<DriveDownloadedFile> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"{ApiRoot}/files/{Uri.EscapeDataString(fileId)}?alt=media",
            content: null,
            expectedETag: null,
            cancellationToken);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var metadata = await GetMetadataAsync(fileId, cancellationToken);
        var etag = response.Headers.ETag?.ToString();
        if (!string.IsNullOrWhiteSpace(etag))
        {
            metadata = metadata with { ETag = etag };
        }

        return new DriveDownloadedFile { File = metadata, Content = content };
    }

    public async Task<DriveRemoteFile> CreateAsync(
        string name,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        var boundary = $"mednote_{Guid.NewGuid():N}";
        using var multipart = new MultipartContent("related", boundary);
        multipart.Add(new StringContent(
            JsonSerializer.Serialize(new
            {
                name,
                mimeType = "application/json",
                parents = new[] { "appDataFolder" },
                appProperties = new Dictionary<string, string> { ["mednoteId"] = "manifest:v2" },
            }, _json),
            Encoding.UTF8,
            "application/json"));
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(body);
        using var response = await SendAsync(
            HttpMethod.Post,
            $"{UploadRoot}/files?uploadType=multipart&fields=id,name,version,modifiedTime",
            multipart,
            expectedETag: null,
            cancellationToken);
        var created = await ParseFileAsync(response, cancellationToken);
        return string.IsNullOrWhiteSpace(created.ETag)
            ? await GetMetadataAsync(created.Id, cancellationToken)
            : created;
    }

    public async Task<DriveRemoteFile> UpdateAsync(
        string fileId,
        string expectedETag,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedETag);
        ArgumentNullException.ThrowIfNull(content);
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await SendAsync(
            HttpMethod.Patch,
            $"{UploadRoot}/files/{Uri.EscapeDataString(fileId)}?uploadType=media&fields=id,name,version,modifiedTime",
            body,
            expectedETag,
            cancellationToken);
        var updated = await ParseFileAsync(response, cancellationToken);
        return string.IsNullOrWhiteSpace(updated.ETag)
            ? await GetMetadataAsync(updated.Id, cancellationToken)
            : updated;
    }

    private async Task<DriveRemoteFile> GetMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"{ApiRoot}/files/{Uri.EscapeDataString(fileId)}?fields=id,name,version,modifiedTime",
            content: null,
            expectedETag: null,
            cancellationToken);
        return await ParseFileAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string uri,
        HttpContent? content,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _accessToken(cancellationToken));
        if (!string.IsNullOrWhiteSpace(expectedETag))
        {
            request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            response.Dispose();
            throw new DriveRemoteChangedException(
                "Bản lưu Google Drive đã thay đổi trên thiết bị khác; không ghi đè tự động.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadErrorAsync(response, cancellationToken);
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Google Drive trả về lỗi {status}: {detail}");
        }

        return response;
    }

    private async Task<DriveRemoteFile> ParseFileAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<DriveFilePayload>(_json, cancellationToken)
            ?? throw new InvalidDataException("Google Drive không trả về metadata tệp.");
        return new DriveRemoteFile
        {
            Id = payload.Id,
            Name = payload.Name,
            Version = payload.Version,
            ModifiedAt = DateTimeOffset.TryParse(payload.ModifiedTime, out var modified) ? modified.ToUnixTimeMilliseconds() : 0,
            ETag = response.Headers.ETag?.ToString() ?? string.Empty,
        };
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<DriveError>(cancellationToken: cancellationToken);
            return payload?.Error?.Message ?? response.ReasonPhrase ?? "Không rõ nguyên nhân";
        }
        catch
        {
            return response.ReasonPhrase ?? "Không rõ nguyên nhân";
        }
    }

    private sealed record DriveFileList
    {
        public List<DriveFilePayload> Files { get; init; } = [];
    }

    private sealed record DriveFilePayload
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string ModifiedTime { get; init; } = string.Empty;
    }

    private sealed record DriveError
    {
        public DriveErrorDetail? Error { get; init; }
    }

    private sealed record DriveErrorDetail
    {
        public string Message { get; init; } = string.Empty;
    }
}
