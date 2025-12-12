namespace VimeoVideoDownloader.Models;

public record DownloadRequest
{
    public string Url { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string PreferredFormat { get; init; } = "mp4";
    public string? PreferredResolution { get; init; }
}
