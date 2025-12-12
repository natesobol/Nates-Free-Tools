using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/extract", async Task<IResult> (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data with one or more files." });
    }

    var form = await request.ReadFormAsync();
    var files = form.Files;

    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "No files were uploaded." });
    }

    var minDurationSeconds = double.TryParse(form["minDurationSeconds"].FirstOrDefault(), out var durationFilter)
        ? Math.Max(0, durationFilter)
        : 0;
    var minFrequency = int.TryParse(form["minFrequency"].FirstOrDefault(), out var freqFilter)
        ? Math.Max(1, freqFilter)
        : 1;

    var occurrences = new List<TimestampOccurrence>();
    var errors = new List<object>();

    foreach (var file in files)
    {
        try
        {
            var text = await ReadFileTextAsync(file);
            occurrences.AddRange(ExtractTimestamps(text, file.FileName));
        }
        catch (Exception ex)
        {
            errors.Add(new { file = file.FileName, message = ex.Message });
        }
    }

    var grouped = occurrences.GroupBy(o => o.Key)
        .ToDictionary(g => g.Key, g => g.Count());

    var filtered = occurrences
        .Where(o => (!o.DurationSeconds.HasValue || o.DurationSeconds.Value >= minDurationSeconds)
            && grouped[o.Key] >= minFrequency)
        .Select(o => new TimestampResult
        {
            File = o.File,
            Type = o.Type,
            Start = o.Start,
            End = o.End,
            Timestamp = o.Timestamp,
            DurationSeconds = o.DurationSeconds,
            DurationHuman = o.DurationSeconds.HasValue ? FormatDuration(o.DurationSeconds.Value) : null,
            Context = o.Context,
            Frequency = grouped[o.Key]
        })
        .ToList();

    return Results.Ok(new
    {
        count = filtered.Count,
        filters = new { minDurationSeconds, minFrequency },
        matches = filtered,
        errors
    });
});

app.Run();

static async Task<string> ReadFileTextAsync(IFormFile file)
{
    using var stream = file.OpenReadStream();
    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
    return await reader.ReadToEndAsync();
}

static IEnumerable<TimestampOccurrence> ExtractTimestamps(string content, string fileName)
{
    var results = new List<TimestampOccurrence>();

    var rangePattern = new Regex(
        "(?<start>\\d{1,2}:\\d{2}:\\d{2}(?:[.,]\\d{1,3})?)\\s*-->\\s*(?<end>\\d{1,2}:\\d{2}:\\d{2}(?:[.,]\\d{1,3})?)",
        RegexOptions.Multiline);

    var occupiedSpans = new List<(int Start, int End)>();

    foreach (Match match in rangePattern.Matches(content))
    {
        var start = match.Groups["start"].Value;
        var end = match.Groups["end"].Value;
        var durationSeconds = ComputeDurationSeconds(start, end);
        var context = ExtractContext(content, match.Index);

        results.Add(new TimestampOccurrence
        {
            File = fileName,
            Type = "Range",
            Start = start,
            End = end,
            Timestamp = $"{start} --> {end}",
            DurationSeconds = durationSeconds,
            Context = context,
            Key = $"{start}|{end}"
        });

        occupiedSpans.Add((match.Index, match.Index + match.Length));
    }

    var patterns = new List<(Regex Regex, string Type)>
    {
        (new Regex("\\b\\d{1,2}:\\d{2}:\\d{2}(?:[.,]\\d{1,3})?\\b"), "Timestamp"),
        (new Regex("\\b\\d{1,2}:\\d{2}(?::\\d{2})?\\s?(?:AM|PM|am|pm)\\b"), "Time of day")
    };

    foreach (var (regex, type) in patterns)
    {
        foreach (Match match in regex.Matches(content))
        {
            if (IsInsideSpan(match.Index, occupiedSpans))
            {
                continue;
            }

            var value = match.Value;
            results.Add(new TimestampOccurrence
            {
                File = fileName,
                Type = type,
                Timestamp = value,
                Start = value,
                End = null,
                DurationSeconds = null,
                Context = ExtractContext(content, match.Index),
                Key = value.ToUpperInvariant()
            });
        }
    }

    return results;
}

static bool IsInsideSpan(int index, List<(int Start, int End)> spans)
{
    foreach (var span in spans)
    {
        if (index >= span.Start && index < span.End)
        {
            return true;
        }
    }

    return false;
}

static double? ComputeDurationSeconds(string start, string end)
{
    if (TryParseTimestamp(start, out var startTime) && TryParseTimestamp(end, out var endTime))
    {
        var duration = endTime - startTime;
        return Math.Abs(duration.TotalSeconds);
    }

    return null;
}

static bool TryParseTimestamp(string value, out TimeSpan time)
{
    var normalized = value.Replace(',', '.');

    var formats = new[]
    {
        "h\\:mm\\:ss\.FFF", "hh\\:mm\\:ss\.FFF", "h\\:mm\\:ss", "hh\\:mm\\:ss", "m\\:ss", "mm\\:ss"
    };

    if (TimeSpan.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, out time))
    {
        return true;
    }

    if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
    {
        time = dt.TimeOfDay;
        return true;
    }

    time = default;
    return false;
}

static string ExtractContext(string content, int index)
{
    var start = content.LastIndexOf('\n', index >= content.Length ? content.Length - 1 : index);
    start = start == -1 ? 0 : start + 1;

    var end = content.IndexOf('\n', index);
    end = end == -1 ? content.Length : end;

    var line = content[start..end].Trim();
    return line.Length > 180 ? line[..180] + "…" : line;
}

static string FormatDuration(double seconds)
{
    var ts = TimeSpan.FromSeconds(seconds);
    var builder = new StringBuilder();

    if (ts.Hours > 0)
    {
        builder.Append(ts.Hours).Append('h').Append(' ');
    }

    builder.Append(ts.Minutes).Append('m ');
    builder.Append(ts.Seconds).Append('s');

    if (ts.Milliseconds > 0)
    {
        builder.Append(' ').Append(ts.Milliseconds).Append("ms");
    }

    return builder.ToString().Trim();
}

record TimestampOccurrence
{
    public required string File { get; init; }
    public required string Type { get; init; }
    public required string Timestamp { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public double? DurationSeconds { get; init; }
    public string? Context { get; init; }
    public required string Key { get; init; }
}

record TimestampResult
{
    public required string File { get; init; }
    public required string Type { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public string? Timestamp { get; init; }
    public double? DurationSeconds { get; init; }
    public string? DurationHuman { get; init; }
    public string? Context { get; init; }
    public int Frequency { get; init; }
}
