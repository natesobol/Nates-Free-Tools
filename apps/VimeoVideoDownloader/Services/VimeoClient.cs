using System.Text.Json;
using System.Text.RegularExpressions;
using VimeoVideoDownloader.Models;

namespace VimeoVideoDownloader.Services;

public class VimeoClient
{
    private static readonly Regex VideoIdRegex = new(@"vimeo\.com/(?:video/)?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IHttpClientFactory _httpClientFactory;

    public VimeoClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<DownloadOption>> GetDownloadOptionsAsync(DownloadRequest request)
    {
        var config = await FetchConfigAsync(request);
        var progressive = config?.Request?.Files?.Progressive ?? new List<VimeoProgressiveFile>();

        return progressive
            .Where(file => !string.IsNullOrWhiteSpace(file.Url))
            .Select(file => new DownloadOption
            {
                Quality = file.Quality ?? "unknown",
                MimeType = file.Mime,
                Url = file.Url!,
                Width = file.Width,
                Extension = ParseExtension(file.Mime)
            })
            .OrderByDescending(opt => opt.Width)
            .ToList();
    }

    public async Task<DownloadOption?> SelectFileAsync(DownloadRequest request)
    {
        var options = await GetDownloadOptionsAsync(request);
        IEnumerable<DownloadOption> filtered = options;

        if (!string.IsNullOrWhiteSpace(request.PreferredFormat))
        {
            var format = request.PreferredFormat.Trim().ToLowerInvariant();
            filtered = filtered.Where(o => (o.MimeType ?? string.Empty).ToLowerInvariant().Contains(format) ||
                                           (o.Extension ?? string.Empty).ToLowerInvariant().Equals(format, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredResolution))
        {
            var resolution = request.PreferredResolution.Trim().ToLowerInvariant();
            filtered = filtered.Where(o => o.Quality.ToLowerInvariant().Contains(resolution));
        }

        return filtered.FirstOrDefault() ?? options.FirstOrDefault();
    }

    public async Task<Stream> GetVideoStreamAsync(string url)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; VimeoDownloader/1.0)");
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    public string BuildFileName(DownloadOption option, string sourceUrl)
    {
        var videoId = ExtractVideoId(sourceUrl) ?? "video";
        var extension = option.Extension ?? "mp4";
        var quality = string.IsNullOrWhiteSpace(option.Quality) ? string.Empty : $"-{option.Quality}";
        return $"vimeo-{videoId}{quality}.{extension}";
    }

    private async Task<VimeoConfig?> FetchConfigAsync(DownloadRequest request)
    {
        var videoId = ExtractVideoId(request.Url) ?? throw new ArgumentException("Unable to find a Vimeo video ID in the provided URL.");
        var configUrl = BuildConfigUrl(videoId, request.Password);

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; VimeoDownloader/1.0)");

        var response = await client.GetAsync(configUrl);

        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Vimeo returned {(int)response.StatusCode}: {reason}");
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<VimeoConfig>(contentStream);
    }

    private static string BuildConfigUrl(string videoId, string? password)
    {
        var url = $"https://player.vimeo.com/video/{videoId}/config";
        if (!string.IsNullOrWhiteSpace(password))
        {
            url += $"?password={Uri.EscapeDataString(password)}";
        }

        return url;
    }

    private static string? ExtractVideoId(string url)
    {
        var match = VideoIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ParseExtension(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return null;
        }

        return mime.ToLowerInvariant() switch
        {
            "video/mp4" => "mp4",
            "video/quicktime" => "mov",
            _ => null
        };
    }
}
