using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/convert", (ConversionRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "Provide text to tabify or untabify." });
    }

    var mode = NormalizeMode(request.Mode);
    var spacesPerTab = Math.Clamp(request.SpacesPerTab, 1, 8);
    var trimmed = request.TrimTrailingWhitespace;

    var normalizedInput = trimmed ? TrimTrailingWhitespace(request.Text) : request.Text;
    var converted = mode == ConversionMode.SpacesToTabs
        ? SpacesToTabs(normalizedInput, spacesPerTab)
        : TabsToSpaces(normalizedInput, spacesPerTab);

    var stats = new
    {
        tabsToSpaces = mode == ConversionMode.TabsToSpaces,
        spacesPerTab,
        inputLength = request.Text.Length,
        outputLength = converted.Length,
        tabsFound = CountTabs(request.Text),
        spaceRunsConverted = CountSpaceRuns(request.Text, spacesPerTab)
    };

    return Results.Ok(new
    {
        mode = mode.ToString(),
        trimTrailingWhitespace = trimmed,
        stats,
        converted,
        preview = BuildPreview(converted)
    });
});

app.Run();

enum ConversionMode
{
    TabsToSpaces,
    SpacesToTabs
}

record ConversionRequest
{
    public string? Text { get; init; }
    public string? Mode { get; init; }
    public int SpacesPerTab { get; init; } = 4;
    public bool TrimTrailingWhitespace { get; init; } = true;
}

static ConversionMode NormalizeMode(string? mode)
{
    return mode?.ToLowerInvariant() switch
    {
        "spaces-to-tabs" => ConversionMode.SpacesToTabs,
        _ => ConversionMode.TabsToSpaces
    };
}

static string TabsToSpaces(string text, int spacesPerTab)
{
    return text.Replace("\t", new string(' ', spacesPerTab));
}

static string SpacesToTabs(string text, int spacesPerTab)
{
    var pattern = new string(' ', spacesPerTab);
    return Regex.Replace(text, Regex.Escape(pattern), "\t");
}

static string TrimTrailingWhitespace(string text)
{
    var lines = text.Replace("\r\n", "\n").Split('\n');
    for (var i = 0; i < lines.Length; i++)
    {
        lines[i] = lines[i].TrimEnd(' ', '\t');
    }

    return string.Join("\n", lines);
}

static int CountTabs(string text) => text.Count(c => c == '\t');

static int CountSpaceRuns(string text, int spacesPerTab)
{
    if (spacesPerTab <= 0)
    {
        return 0;
    }

    var pattern = new string(' ', spacesPerTab);
    return Regex.Matches(text, Regex.Escape(pattern)).Count;
}

static string BuildPreview(string converted)
{
    var lines = converted.Replace("\r\n", "\n").Split('\n');
    return string.Join("\n", lines.Take(5));
}
