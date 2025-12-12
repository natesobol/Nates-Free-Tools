namespace VimeoVideoDownloader.Models;

public record DownloadOption
{
    public string Quality { get; init; } = string.Empty;
    public string? MimeType { get; init; }
    public string Url { get; init; } = string.Empty;
    public int? Width { get; init; }
    public string? Extension { get; init; }
}
