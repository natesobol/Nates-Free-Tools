using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/extract", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();

    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { error = "Upload at least one .html, .md, or .txt file." });
    }

    var allowed = new HashSet<string>(new[] { ".html", ".htm", ".md", ".markdown", ".txt" }, StringComparer.OrdinalIgnoreCase);

    var htmlImgRegex = new Regex("<img[^>]*?\\s+src\\s*=\\s*[\\\"'](?<src>[^\\"'>]+)[\\"'][^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    var markdownRegex = new Regex("!\\[[^\\]]*\\]\\((?<url>[^)\\s]+)(?:\\s+\\\"[^\\\"]*\\\")?\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    var textPathRegex = new Regex("(?<path>(?:https?://|(?:\\.{1,2}/|/)?)[\\w@:%./+~-]+?\\.(?:png|jpe?g|gif|svg|webp|bmp|tiff|ico|avif))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    var totalMatches = 0;
    var results = new List<object>();

    foreach (var file in form.Files)
    {
        var extension = Path.GetExtension(file.FileName);

        if (!allowed.Contains(extension))
        {
            results.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                matches = Array.Empty<object>(),
                error = "Unsupported file type. Upload .html, .md, or .txt files."
            });
            continue;
        }

        if (file.Length == 0)
        {
            results.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                matches = Array.Empty<object>(),
                error = "File was empty."
            });
            continue;
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();

            var matches = ExtractMatches(content, htmlImgRegex, markdownRegex, textPathRegex);
            totalMatches += matches.Count;

            results.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                matches = matches
                    .OrderBy(m => m.Line)
                    .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new { m.Path, m.Syntax, m.Line }),
                error = (string?)null
            });
        }
        catch (Exception ex)
        {
            results.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                matches = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }

    return Results.Ok(new
    {
        filesProcessed = results.Count,
        totalMatches,
        results
    });
});

app.Run();

static List<ImageMatch> ExtractMatches(string content, Regex htmlImgRegex, Regex markdownRegex, Regex textPathRegex)
{
    var matches = new List<ImageMatch>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    for (var i = 0; i < lines.Length; i++)
    {
        var lineNumber = i + 1;
        var line = lines[i];

        AddMatchesFromRegex(htmlImgRegex, line, "HTML <img>", lineNumber, matches, seen);
        AddMatchesFromRegex(markdownRegex, line, "Markdown image", lineNumber, matches, seen);
        AddMatchesFromRegex(textPathRegex, line, "Plain text path", lineNumber, matches, seen);
    }

    return matches;
}

static void AddMatchesFromRegex(Regex regex, string line, string syntax, int lineNumber, List<ImageMatch> matches, HashSet<string> seen)
{
    foreach (Match match in regex.Matches(line))
    {
        var value = match.Groups["src"].Success
            ? match.Groups["src"].Value
            : match.Groups["url"].Success
                ? match.Groups["url"].Value
                : match.Groups["path"].Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        var trimmed = value.Trim();
        var key = $"{syntax}::{lineNumber}::{trimmed}";

        if (seen.Add(key))
        {
            matches.Add(new ImageMatch(trimmed, syntax, lineNumber));
        }
    }
}

record ImageMatch(string Path, string Syntax, int Line);
