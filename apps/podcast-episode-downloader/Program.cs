using System.IO.Compression;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

builder.Services.AddHttpClient("feeds", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PodcastEpisodeDownloader/1.0");
});

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/fetch-episodes", async Task<IResult> (FeedRequest request, IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var feedUri))
    {
        return Results.BadRequest(new { error = "Provide a valid http(s) URL." });
    }

    var client = httpClientFactory.CreateClient("feeds");

    try
    {
        using var response = await client.GetAsync(feedUri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return Results.BadRequest(new { error = $"Failed to fetch feed: {(int)response.StatusCode} {response.ReasonPhrase}" });
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        var episodes = TryParseFeed(bytes, feedUri);

        if (episodes.Count == 0 && IsLikelyAudioUrl(feedUri, contentType))
        {
            episodes = new List<EpisodeInfo>
            {
                new(
                    Title: GuessTitleFromUrl(feedUri),
                    Published: DateTimeOffset.UtcNow,
                    Duration: null,
                    AudioUrl: feedUri.ToString()
                )
            };
        }

        if (episodes.Count == 0)
        {
            return Results.BadRequest(new { error = "No downloadable episodes were found at that location." });
        }

        return Results.Ok(new
        {
            source = feedUri.ToString(),
            episodes
        });
    }
    catch (TaskCanceledException)
    {
        return Results.BadRequest(new { error = "The request timed out while fetching the feed." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Unable to read the feed: {ex.Message}" });
    }
});

app.MapPost("/api/download", async Task<IResult> (DownloadRequest request, IHttpClientFactory httpClientFactory) =>
{
    if (request.Episodes is null || request.Episodes.Count == 0)
    {
        return Results.BadRequest(new { error = "Submit at least one episode to download." });
    }

    var validEpisodes = request.Episodes
        .Where(e => !string.IsNullOrWhiteSpace(e.Url) && Uri.TryCreate(e.Url, UriKind.Absolute, out _))
        .ToList();

    if (validEpisodes.Count == 0)
    {
        return Results.BadRequest(new { error = "None of the provided episode URLs were valid." });
    }

    var client = httpClientFactory.CreateClient("feeds");

    if (request.BundleAsZip || validEpisodes.Count > 1)
    {
        var archiveStream = new MemoryStream();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);
        var errors = new List<string>();

        foreach (var episode in validEpisodes)
        {
            try
            {
                var targetUri = new Uri(episode.Url);
                await using var audioStream = await client.GetStreamAsync(targetUri);
                var entryName = BuildFileName(episode, targetUri);
                var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
                await using var entryStream = entry.Open();
                await audioStream.CopyToAsync(entryStream);
            }
            catch (Exception ex)
            {
                errors.Add($"{episode.Title ?? episode.Url}: {ex.Message}");
            }
        }

        if (errors.Count == validEpisodes.Count)
        {
            return Results.BadRequest(new { error = "Failed to download all selected episodes.", details = errors });
        }

        archiveStream.Position = 0;

        return Results.File(
            fileContents: archiveStream,
            contentType: "application/zip",
            fileDownloadName: request.ZipName ?? "podcast-episodes.zip",
            enableRangeProcessing: true);
    }

    var single = validEpisodes[0];
    try
    {
        var targetUri = new Uri(single.Url);
        var data = await client.GetByteArrayAsync(targetUri);
        var fileName = BuildFileName(single, targetUri);
        var contentType = "audio/mpeg";

        return Results.File(data, contentType, fileName, enableRangeProcessing: true);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Failed to download episode: {ex.Message}" });
    }
});

app.Run();

static List<EpisodeInfo> TryParseFeed(byte[] payload, Uri feedUri)
{
    var episodes = new List<EpisodeInfo>();

    try
    {
        using var reader = XmlReader.Create(new MemoryStream(payload), new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore
        });

        var feed = SyndicationFeed.Load(reader);
        if (feed?.Items is null)
        {
            return episodes;
        }

        foreach (var item in feed.Items)
        {
            var enclosure = item.Links.FirstOrDefault(l =>
                string.Equals(l.RelationshipType, "enclosure", StringComparison.OrdinalIgnoreCase) &&
                (IsAudioMediaType(l.MediaType) || IsAudioUrl(l.Uri)));

            if (enclosure is null && item.Links.Any())
            {
                enclosure = item.Links.FirstOrDefault(l => IsAudioUrl(l.Uri));
            }

            if (enclosure?.Uri is null)
            {
                continue;
            }

            episodes.Add(new EpisodeInfo(
                Title: item.Title?.Text ?? GuessTitleFromUrl(enclosure.Uri),
                Published: item.PublishDate != default ? item.PublishDate : feed.LastUpdatedTime,
                Duration: ReadDuration(item),
                AudioUrl: enclosure.Uri.ToString()
            ));
        }
    }
    catch
    {
        return episodes;
    }

    return episodes
        .Where(e => !string.IsNullOrWhiteSpace(e.AudioUrl))
        .OrderByDescending(e => e.Published)
        .ToList();
}

static bool IsAudioMediaType(string? mediaType)
{
    return !string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("audio", StringComparison.OrdinalIgnoreCase);
}

static bool IsAudioUrl(Uri uri)
{
    var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
    return extension is ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" or ".mp4";
}

static bool IsLikelyAudioUrl(Uri uri, string contentType)
{
    return IsAudioMediaType(contentType) || IsAudioUrl(uri);
}

static string GuessTitleFromUrl(Uri uri)
{
    var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return uri.Host;
    }

    var cleaned = Regex.Replace(fileName, "[-_]+", " ").Trim();
    return string.IsNullOrWhiteSpace(cleaned) ? uri.Host : cleaned;
}

static string? ReadDuration(SyndicationItem item)
{
    const string itunesNamespace = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    try
    {
        var durations = item.ElementExtensions.ReadElementExtensions<string>("duration", itunesNamespace);
        var raw = durations.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TimeSpan.TryParse(raw, out var parsed))
        {
            return parsed.ToString();
        }

        if (int.TryParse(raw, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds).ToString();
        }
    }
    catch
    {
        // Ignore parsing errors and return null
    }

    return null;
}

static string BuildFileName(EpisodeDownload episode, Uri uri)
{
    var safeTitle = string.IsNullOrWhiteSpace(episode.Title)
        ? GuessTitleFromUrl(uri)
        : episode.Title;

    safeTitle = SanitizeFileName(safeTitle);

    var extension = Path.GetExtension(uri.AbsolutePath);
    if (string.IsNullOrWhiteSpace(extension))
    {
        extension = ".mp3";
    }

    return safeTitle + extension;
}

static string SanitizeFileName(string name)
{
    foreach (var invalidChar in Path.GetInvalidFileNameChars())
    {
        name = name.Replace(invalidChar, '-');
    }

    var cleaned = Regex.Replace(name, "\s+", " ").Trim();
    return string.IsNullOrWhiteSpace(cleaned) ? "episode" : cleaned;
}

record FeedRequest(string Url);

record DownloadRequest(List<EpisodeDownload> Episodes, bool BundleAsZip = false, string? ZipName = null);

record EpisodeDownload(string Url, string? Title);

record EpisodeInfo(string Title, DateTimeOffset Published, string? Duration, string AudioUrl);
