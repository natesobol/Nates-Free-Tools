using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/extract", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var caseSensitive = ParseBool(form["caseSensitive"], false);
    var deduplicate = ParseBool(form["deduplicate"], false);

    var allHashtags = new List<string>();
    var allMentions = new List<string>();

    var items = new List<object>();

    if (!string.IsNullOrWhiteSpace(form["text"]))
    {
        var inlineText = form["text"].ToString();
        var (hashtags, mentions) = ExtractHashtagsAndMentions(inlineText, caseSensitive, deduplicate);

        allHashtags.AddRange(hashtags);
        allMentions.AddRange(mentions);

        items.Add(new
        {
            source = "Inline text",
            kind = "text",
            hashtagCount = hashtags.Count,
            mentionCount = mentions.Count,
            hashtags,
            mentions
        });
    }

    foreach (var file in form.Files)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        var item = new
        {
            source = file.FileName,
            kind = extension.TrimStart('.'),
            hashtagCount = 0,
            mentionCount = 0,
            hashtags = Array.Empty<string>(),
            mentions = Array.Empty<string>(),
            error = (string?)null
        };

        try
        {
            var content = extension switch
            {
                ".txt" or ".csv" or ".json" => await ReadAsStringAsync(file),
                _ => throw new InvalidOperationException("Unsupported file type. Upload .txt, .csv, or .json."),
            };

            var (hashtags, mentions) = ExtractHashtagsAndMentions(content, caseSensitive, deduplicate);

            allHashtags.AddRange(hashtags);
            allMentions.AddRange(mentions);

            item = new
            {
                item.source,
                item.kind,
                hashtagCount = hashtags.Count,
                mentionCount = mentions.Count,
                hashtags,
                mentions,
                error = (string?)null
            };
        }
        catch (Exception ex)
        {
            item = new
            {
                item.source,
                item.kind,
                item.hashtagCount,
                item.mentionCount,
                item.hashtags,
                item.mentions,
                error = ex.Message
            };
        }

        items.Add(item);
    }

    if (!items.Any())
    {
        return Results.BadRequest(new { error = "Upload at least one file or provide inline text." });
    }

    var hashtagFrequency = BuildFrequency(allHashtags);
    var mentionFrequency = BuildFrequency(allMentions);

    return Results.Ok(new
    {
        caseSensitive,
        deduplicate,
        totals = new
        {
            hashtags = allHashtags.Count,
            mentions = allMentions.Count,
            inputs = items.Count
        },
        hashtagFrequency,
        mentionFrequency,
        results = items
    });
});

app.Run();

static bool ParseBool(string? value, bool defaultValue)
{
    if (bool.TryParse(value, out var parsed))
    {
        return parsed;
    }

    return defaultValue;
}

static async Task<string> ReadAsStringAsync(IFormFile file)
{
    using var reader = new StreamReader(file.OpenReadStream());
    return await reader.ReadToEndAsync();
}

static (List<string> hashtags, List<string> mentions) ExtractHashtagsAndMentions(string content, bool caseSensitive, bool deduplicate)
{
    var options = RegexOptions.Compiled | RegexOptions.Multiline;
    if (!caseSensitive)
    {
        options |= RegexOptions.IgnoreCase;
    }

    var hashtagPattern = new Regex(@"(?<!\w)#([\p{L}\p{N}_]{1,100})", options);
    var mentionPattern = new Regex(@"(?<!\w)@([A-Za-z0-9_.]{1,100})", options);

    var hashtags = hashtagPattern.Matches(content)
        .Select(match => NormalizeToken(match.Value, caseSensitive))
        .ToList();

    var mentions = mentionPattern.Matches(content)
        .Select(match => NormalizeToken(match.Value, caseSensitive))
        .ToList();

    if (deduplicate)
    {
        hashtags = hashtags.Distinct().ToList();
        mentions = mentions.Distinct().ToList();
    }

    return (hashtags, mentions);
}

static List<object> BuildFrequency(IEnumerable<string> values)
{
    return values
        .GroupBy(value => value)
        .Select(group => new { value = group.Key, count = group.Count() })
        .OrderByDescending(item => item.count)
        .ThenBy(item => item.value)
        .Cast<object>()
        .ToList();
}

static string NormalizeToken(string token, bool caseSensitive)
{
    return caseSensitive ? token.Trim() : token.Trim().ToLowerInvariant();
}
