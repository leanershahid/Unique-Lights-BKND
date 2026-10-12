using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Exceptions;

namespace UniqueLights.Api.Services;

public sealed record StoredFile(string Path, string PublicUrl);

public interface IStorageService
{
    bool IsConfigured { get; }
    Task<StoredFile> UploadAsync(string path, Stream content, string contentType, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    string GetPublicUrl(string path);
}

/// <summary>Supabase Storage REST client (typed HttpClient). Uses the service role key; never expose it to browsers.</summary>
public sealed class SupabaseStorageService(HttpClient http, IOptions<SupabaseOptions> options, ILogger<SupabaseStorageService> logger)
    : IStorageService
{
    private readonly SupabaseOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    private string BaseUrl => _options.Url.TrimEnd('/');

    public async Task<StoredFile> UploadAsync(string path, Stream content, string contentType, CancellationToken ct)
    {
        EnsureConfigured();
        var url = $"{BaseUrl}/storage/v1/object/{Uri.EscapeDataString(_options.Bucket)}/{EscapePath(path)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuth(request);
        request.Headers.Add("x-upsert", "true");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Supabase upload failed ({StatusCode}) for {Path}: {Body}", (int)response.StatusCode, path, body);
            throw new ServiceUnavailableException("Image upload failed. Please try again later.");
        }

        return new StoredFile(path, GetPublicUrl(path));
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        EnsureConfigured();
        var url = $"{BaseUrl}/storage/v1/object/{Uri.EscapeDataString(_options.Bucket)}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        AddAuth(request);
        request.Content = JsonContent.Create(new { prefixes = new[] { path } });

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Supabase delete failed ({StatusCode}) for {Path}: {Body}", (int)response.StatusCode, path, body);
            throw new ServiceUnavailableException("Image could not be deleted from storage.");
        }
    }

    public string GetPublicUrl(string path) =>
        $"{BaseUrl}/storage/v1/object/public/{Uri.EscapeDataString(_options.Bucket)}/{EscapePath(path)}";

    private void AddAuth(HttpRequestMessage request)
    {
        // New Supabase secret keys (sb_secret_...) are not JWTs and must only be sent in the apikey header;
        // the API gateway rejects them as a Bearer token. Legacy service_role keys are JWTs and need both headers.
        request.Headers.Add("apikey", _options.ServiceKey);
        if (_options.ServiceKey.StartsWith("eyJ", StringComparison.Ordinal))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceKey);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new ServiceUnavailableException("Image storage is not configured");
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}

/// <summary>Validates uploaded product images by extension, declared size and magic bytes.</summary>
public static class ImageFileValidator
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    /// <returns>(normalized extension, content type)</returns>
    public static async Task<(string Extension, string ContentType)> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) throw new ValidationException("file", "The file is empty.");
        if (file.Length > MaxBytes) throw new ValidationException("file", "The image must be 5 MB or smaller.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ContentTypes.TryGetValue(extension, out var contentType))
        {
            throw new ValidationException("file", "Only .jpg, .jpeg, .png and .webp images are allowed.");
        }

        var header = new byte[12];
        await using (var stream = file.OpenReadStream())
        {
            var read = 0;
            while (read < header.Length)
            {
                var n = await stream.ReadAsync(header.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read < header.Length) throw new ValidationException("file", "The file is not a valid image.");
        }

        var matches = contentType switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                           header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "image/webp" => header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                            header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P',
            _ => false
        };
        if (!matches) throw new ValidationException("file", "The file content does not match its extension.");

        return (extension == ".jpeg" ? ".jpg" : extension, contentType);
    }
}
