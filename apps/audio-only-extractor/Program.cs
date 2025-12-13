using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Extensions.Downloader;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

var workingDirectory = Path.Combine(Path.GetTempPath(), "audio-only-extractor");
Directory.CreateDirectory(workingDirectory);

var ytDlpPathTask = EnsureYtDlpAsync(workingDirectory);
var ffmpegPathTask = EnsureFfmpegAsync(workingDirectory);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/extract-audio", async Task<IResult> (AudioExtractionRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "Provide a video URL." });
    }

    if (!IsSupportedPlatform(request.Url))
    {
        return Results.BadRequest(new { error = "Only YouTube, Vimeo, or TikTok links are supported." });
    }

    var format = request.Format?.Trim().ToLowerInvariant();
    if (format is null || !SupportedFormats.All.Contains(format))
    {
        return Results.BadRequest(new { error = "Choose a supported format: mp3, wav, or aac." });
    }

    if (request.TrimStartSeconds is < 0)
    {
        return Results.BadRequest(new { error = "Trim start must be zero or greater." });
    }

    if (request.TrimEndSeconds is < 0)
    {
        return Results.BadRequest(new { error = "Trim end must be zero or greater." });
    }

    if (request.TrimStartSeconds is not null && request.TrimEndSeconds is not null && request.TrimEndSeconds <= request.TrimStartSeconds)
    {
        return Results.BadRequest(new { error = "Trim end must be greater than trim start when both are provided." });
    }

    var ytDlpPath = await ytDlpPathTask;
    if (ytDlpPath is null)
    {
        return Results.BadRequest(new { error = "Unable to download the yt-dlp helper. Try again in a moment." });
    }

    var ffmpegPath = await ffmpegPathTask;
    if (ffmpegPath is null)
    {
        return Results.BadRequest(new { error = "Unable to download the ffmpeg dependency." });
    }

    var jobId = Guid.NewGuid().ToString("N");
    var downloadBase = Path.Combine(workingDirectory, jobId);
    var downloadTemplate = downloadBase + ".%(ext)s";

    var downloadResult = await RunCommandAsync(ytDlpPath, $"-f bestaudio --no-playlist --no-progress -o \"{downloadTemplate}\" \"{request.Url}\"");

    if (!downloadResult.Success)
    {
        var error = string.IsNullOrWhiteSpace(downloadResult.Error) ? "Unknown error." : downloadResult.Error;
        return Results.BadRequest(new { error = $"Failed to download audio: {error}" });
    }

    var downloadedFile = Directory.GetFiles(workingDirectory, jobId + ".*").FirstOrDefault();

    if (downloadedFile is null)
    {
        return Results.BadRequest(new { error = "Download did not produce an audio file." });
    }

    var outputPath = Path.Combine(workingDirectory, $"{jobId}-clean.{format}");
    var ffmpegArguments = BuildFfmpegArguments(downloadedFile, outputPath, format, request);

    var ffmpegResult = await RunCommandAsync(ffmpegPath, ffmpegArguments);

    if (!ffmpegResult.Success || !File.Exists(outputPath))
    {
        var message = ffmpegResult.Error ?? "Failed to transcode audio.";
        return Results.BadRequest(new { error = message });
    }

    var downloadName = GetDownloadFileName(request.Url, format);
    var stream = File.OpenRead(outputPath);

    return Results.File(stream, GetMimeType(format), downloadName);
});

app.Run();

static async Task<string?> EnsureYtDlpAsync(string toolsDirectory)
{
    var executable = Path.Combine(toolsDirectory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
    if (File.Exists(executable))
    {
        return executable;
    }

    try
    {
        using var client = new HttpClient();
        var downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        var bytes = await client.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(executable, bytes);

        if (!OperatingSystem.IsWindows())
        {
            var chmodResult = await RunCommandAsync("chmod", $"+x \"{executable}\"");
            if (!chmodResult.Success)
            {
                return null;
            }
        }

        return executable;
    }
    catch
    {
        return null;
    }
}

static async Task<string?> EnsureFfmpegAsync(string toolsDirectory)
{
    var binaryFolder = Path.Combine(toolsDirectory, "ffmpeg");
    Directory.CreateDirectory(binaryFolder);

    var ffmpegExecutable = Path.Combine(binaryFolder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    if (!File.Exists(ffmpegExecutable))
    {
        try
        {
            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = binaryFolder;
                options.TemporaryFilesFolder = toolsDirectory;
            });

            await FFMpegDownloader.GetLatestVersion(FFMpegVersion.Official, binaryFolder);
        }
        catch
        {
            return null;
        }
    }

    return File.Exists(ffmpegExecutable) ? ffmpegExecutable : null;
}

static bool IsSupportedPlatform(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var host = uri.Host.ToLowerInvariant();
    return host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
        || host.Contains("vimeo.com", StringComparison.OrdinalIgnoreCase)
        || host.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);
}

static string GetMimeType(string format) => format.ToLowerInvariant() switch
{
    "mp3" => "audio/mpeg",
    "wav" => "audio/wav",
    "aac" => "audio/aac",
    _ => "application/octet-stream"
};

static string GetDownloadFileName(string url, string format)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return $"audio.{format}";
    }

    var slug = uri.Segments.LastOrDefault()?.Trim('/') ?? "audio";
    if (string.IsNullOrWhiteSpace(slug))
    {
        slug = uri.Host.Replace('.', '-');
    }

    return $"{slug}-audio.{format}";
}

static string BuildFfmpegArguments(string input, string output, string format, AudioExtractionRequest request)
{
    var args = new List<string>
    {
        "-y",
        $"-i \"{input}\"",
        "-vn"
    };

    if (request.TrimStartSeconds is not null)
    {
        args.Add($"-ss {request.TrimStartSeconds.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    if (request.TrimEndSeconds is not null)
    {
        args.Add($"-to {request.TrimEndSeconds.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    var filters = new List<string>();
    if (request.NormalizeLevels)
    {
        filters.Add("loudnorm");
    }

    if (filters.Count > 0)
    {
        args.Add($"-af \"{string.Join(',', filters)}\"");
    }

    args.Add($"-acodec {GetCodec(format)}");
    args.Add($"\"{output}\"");

    return string.Join(' ', args);
}

static string GetCodec(string format) => format.ToLowerInvariant() switch
{
    "mp3" => "libmp3lame",
    "aac" => "aac",
    "wav" => "pcm_s16le",
    _ => "copy"
};

static async Task<CommandResult> RunCommandAsync(string fileName, string arguments)
{
    var tcs = new TaskCompletionSource<CommandResult>();
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        },
        EnableRaisingEvents = true
    };

    var stderr = new List<string>();
    var stdout = new List<string>();

    process.OutputDataReceived += (_, data) =>
    {
        if (data.Data is not null)
        {
            stdout.Add(data.Data);
        }
    };

    process.ErrorDataReceived += (_, data) =>
    {
        if (data.Data is not null)
        {
            stderr.Add(data.Data);
        }
    };

    process.Exited += (_, _) =>
    {
        var success = process.ExitCode == 0;
        var errorMessage = success ? null : string.Join("\n", stderr);
        tcs.TrySetResult(new CommandResult(success, errorMessage, string.Join("\n", stdout)));
        process.Dispose();
    };

    if (!process.Start())
    {
        return new CommandResult(false, $"Failed to start {fileName}.", string.Empty);
    }

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(3)));
    if (completedTask != tcs.Task)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }

        return new CommandResult(false, "Operation timed out.", string.Empty);
    }

    return await tcs.Task;
}

record AudioExtractionRequest
{
    public string Url { get; init; } = string.Empty;
    public string? Format { get; init; }
    public bool NormalizeLevels { get; init; }
    public double? TrimStartSeconds { get; init; }
    public double? TrimEndSeconds { get; init; }
}

record CommandResult(bool Success, string? Error, string StandardOutput);

static class SupportedFormats
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "aac"
    };
}
