using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http.Features;
using UglyToad.PdfPig;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024 * 1024 * 50; // 50 MB
});

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/extract-lists", async Task<IResult> (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data with file uploads." });
    }

    var form = await request.ReadFormAsync();
    var files = form.Files;
    var outputFormat = form["outputFormat"].ToString().ToLowerInvariant();

    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "Upload at least one .docx, .pdf, .txt, or .md file." });
    }

    var responses = new List<ExtractedFile>();

    foreach (var file in files)
    {
        try
        {
            responses.Add(await ProcessFileAsync(file));
        }
        catch (Exception ex)
        {
            responses.Add(new ExtractedFile
            {
                FileName = file.FileName,
                Error = $"Failed to process file: {ex.Message}"
            });
        }
    }

    var totalItems = responses.Sum(r => r.Items?.Count ?? 0);

    return outputFormat switch
    {
        "csv" => Results.File(
            Encoding.UTF8.GetBytes(BuildCsv(responses)),
            "text/csv",
            "list-items.csv"),
        "txt" => Results.File(
            Encoding.UTF8.GetBytes(BuildFlatText(responses)),
            "text/plain",
            "list-items.txt"),
        _ => Results.Ok(new
        {
            files = responses,
            totalItems
        })
    };
});

app.Run();

static async Task<ExtractedFile> ProcessFileAsync(IFormFile file)
{
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    if (extension is ".docx")
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        using var doc = WordprocessingDocument.Open(stream, false);
        var items = ExtractFromWord(doc);

        return new ExtractedFile { FileName = file.FileName, Items = items };
    }

    if (extension is ".pdf")
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        using var pdf = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        var items = ExtractFromPlainText(builder.ToString());
        return new ExtractedFile { FileName = file.FileName, Items = items };
    }

    if (extension is ".txt" or ".md")
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var items = ExtractFromPlainText(content);
        return new ExtractedFile { FileName = file.FileName, Items = items };
    }

    return new ExtractedFile
    {
        FileName = file.FileName,
        Error = "Unsupported file type. Upload .docx, .pdf, .txt, or .md files."
    };
}

static List<StructuredListItem> ExtractFromWord(WordprocessingDocument document)
{
    var items = new List<StructuredListItem>();
    var numberingPart = document.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;

    var counters = new Dictionary<(int NumId, int Level), int>();
    var currentHeading = string.Empty;

    foreach (var paragraph in document.MainDocumentPart!.Document.Body!.Elements<Paragraph>())
    {
        var text = paragraph.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            continue;
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var outlineLevel = (int?)paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;

        if (IsHeading(styleId, outlineLevel))
        {
            currentHeading = text;
            continue;
        }

        var numbering = paragraph.ParagraphProperties?.NumberingProperties;
        if (numbering is null)
        {
            continue;
        }

        var level = (int?)numbering.NumberingLevelReference?.Val?.Value ?? 0;
        var numId = (int?)numbering.NumberingId?.Val?.Value ?? 0;
        var marker = ResolveMarker(numberingPart, numId, level, counters);

        items.Add(new StructuredListItem
        {
            Item = text,
            ListLevel = level + 1,
            Marker = marker,
            ParentHeading = currentHeading
        });
    }

    return items;
}

static bool IsHeading(string? styleId, int? outlineLevel)
{
    if (!string.IsNullOrWhiteSpace(styleId) && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (outlineLevel is not null)
    {
        return true;
    }

    return false;
}

static string ResolveMarker(Numbering? numberingPart, int numId, int level, Dictionary<(int NumId, int Level), int> counters)
{
    if (numberingPart is null)
    {
        return "•";
    }

    var abstractLevel = FindLevel(numberingPart, numId, level);

    if (abstractLevel is null)
    {
        return "•";
    }

    var format = abstractLevel.NumberingFormat?.Val?.Value;

    if (format == NumberFormatValues.Bullet)
    {
        var levelText = abstractLevel.LevelText?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(levelText))
        {
            var replaced = levelText.Replace($"%{level + 1}", "•").Trim();
            return string.IsNullOrWhiteSpace(replaced) ? "•" : replaced;
        }

        var bullet = abstractLevel.LevelSymbol?.Val?.Value;
        return string.IsNullOrWhiteSpace(bullet) ? "•" : bullet!;
    }

    var start = (int?)abstractLevel.StartNumberingValue?.Val?.Value ?? 1;
    var key = (numId, level);
    if (!counters.TryGetValue(key, out var current))
    {
        current = start - 1;
    }

    current++;
    counters[key] = current;

    var formatted = FormatNumber(format, current);
    var levelTextTemplate = abstractLevel.LevelText?.Val?.Value ?? $"%{level + 1}.";

    var marker = levelTextTemplate.Replace($"%{level + 1}", formatted);
    return string.IsNullOrWhiteSpace(marker) ? formatted : marker.Trim();
}

static Level? FindLevel(Numbering numberingPart, int numId, int level)
{
    var numInstance = numberingPart.Elements<NumberingInstance>()
        .FirstOrDefault(n => (int?)n.NumberID?.Value == numId);

    var abstractId = (int?)numInstance?.AbstractNumId?.Val?.Value;
    if (abstractId is null)
    {
        return null;
    }

    var abstractNum = numberingPart.Elements<AbstractNum>()
        .FirstOrDefault(n => (int?)n.AbstractNumberId?.Value == abstractId);

    return abstractNum?.Elements<Level>()
        .FirstOrDefault(l => (int?)l.LevelIndex?.Value == level);
}

static string FormatNumber(NumberFormatValues? format, int number)
{
    return format switch
    {
        NumberFormatValues.LowerLetter => ToAlphabet(number, false),
        NumberFormatValues.UpperLetter => ToAlphabet(number, true),
        NumberFormatValues.LowerRoman => ToRoman(number).ToLowerInvariant(),
        NumberFormatValues.UpperRoman => ToRoman(number),
        _ => number.ToString(CultureInfo.InvariantCulture)
    };
}

static string ToAlphabet(int number, bool upper)
{
    number--;
    var result = new StringBuilder();

    while (number >= 0)
    {
        var remainder = number % 26;
        var letter = (char)(remainder + (upper ? 'A' : 'a'));
        result.Insert(0, letter);
        number = (number / 26) - 1;
    }

    return result.ToString();
}

static string ToRoman(int number)
{
    var map = new (int Value, string Numeral)[]
    {
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    };

    var result = new StringBuilder();
    foreach (var entry in map)
    {
        while (number >= entry.Value)
        {
            result.Append(entry.Numeral);
            number -= entry.Value;
        }
    }

    return result.ToString();
}

static List<StructuredListItem> ExtractFromPlainText(string content)
{
    var items = new List<StructuredListItem>();

    if (string.IsNullOrWhiteSpace(content))
    {
        return items;
    }

    var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var listPattern = new Regex(
        "^(?<indent>\\s*)(?<marker>(?:[\\u2022\\u25E6\\u2043\\u2219•\\-*+]+|\\d+[.)]|\\d+\\)|[a-zA-Z][.)]))\\s+(?<text>.+)$",
        RegexOptions.Compiled);
    var headingPattern = new Regex("^\\s{0,3}#+\\s+(?<heading>.+)$", RegexOptions.Compiled);

    var currentHeading = string.Empty;

    foreach (var raw in lines)
    {
        var line = raw.TrimEnd();

        var headingMatch = headingPattern.Match(line);
        if (headingMatch.Success)
        {
            currentHeading = headingMatch.Groups["heading"].Value.Trim();
            continue;
        }

        var match = listPattern.Match(line);
        if (!match.Success)
        {
            continue;
        }

        var indent = match.Groups["indent"].Value;
        var text = match.Groups["text"].Value.Trim();
        var level = Math.Max(1, (indent.Length / 2) + 1);

        items.Add(new StructuredListItem
        {
            Item = text,
            ListLevel = level,
            Marker = match.Groups["marker"].Value.Trim(),
            ParentHeading = currentHeading
        });
    }

    return items;
}

static string BuildCsv(IEnumerable<ExtractedFile> files)
{
    var builder = new StringBuilder();
    builder.AppendLine("File Name,Item,List Level,Parent Heading");

    foreach (var file in files)
    {
        if (file.Items is null)
        {
            continue;
        }

        foreach (var item in file.Items)
        {
            builder.AppendLine(string.Join(',',
                EscapeCsv(file.FileName),
                EscapeCsv(item.Item),
                item.ListLevel,
                EscapeCsv(item.ParentHeading)));
        }
    }

    return builder.ToString();
}

static string EscapeCsv(string value)
{
    if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    return value;
}

static string BuildFlatText(IEnumerable<ExtractedFile> files)
{
    var builder = new StringBuilder();

    foreach (var file in files)
    {
        builder.AppendLine(file.FileName);
        builder.AppendLine(new string('-', Math.Min(40, file.FileName.Length + 4)));

        if (file.Items is null || file.Items.Count == 0)
        {
            builder.AppendLine("No list items found.");
            builder.AppendLine();
            continue;
        }

        foreach (var item in file.Items)
        {
            var heading = string.IsNullOrWhiteSpace(item.ParentHeading)
                ? "(no heading)"
                : item.ParentHeading;
            builder.AppendLine($"[Level {item.ListLevel}] {heading}: {item.Item}");
        }

        builder.AppendLine();
    }

    return builder.ToString();
}

record ExtractedFile
{
    public string FileName { get; init; } = string.Empty;
    public List<StructuredListItem>? Items { get; init; }
    public string? Error { get; init; }
}

record StructuredListItem
{
    public string Item { get; init; } = string.Empty;
    public string Marker { get; init; } = string.Empty;
    public int ListLevel { get; init; }
    public string ParentHeading { get; init; } = string.Empty;
}
