using System.Text.Json.Serialization;

namespace VimeoVideoDownloader.Models;

public class VimeoConfig
{
    [JsonPropertyName("request")]
    public VimeoRequest? Request { get; set; }
}

public class VimeoRequest
{
    [JsonPropertyName("files")]
    public VimeoFileSet? Files { get; set; }
}

public class VimeoFileSet
{
    [JsonPropertyName("progressive")]
    public List<VimeoProgressiveFile>? Progressive { get; set; }
}

public class VimeoProgressiveFile
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }
}
