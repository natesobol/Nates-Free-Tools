using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

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

    var includeDecimals = ParseBool(form["includeDecimals"], true);
    var includeCurrencySymbols = ParseBool(form["includeCurrencySymbols"], false);
    var ignoreNumbersInWords = ParseBool(form["ignoreNumbersInWords"], true);

    if (form.Files.Count == 0 && string.IsNullOrWhiteSpace(form["text"]))
    {
        return Results.BadRequest(new { error = "Upload at least one file or provide inline text." });
    }

    var pattern = BuildPattern(includeDecimals, includeCurrencySymbols, ignoreNumbersInWords);
    var regex = new Regex(pattern, RegexOptions.Compiled);
    var totalNumbers = 0;

    var items = new List<object>();

    if (!string.IsNullOrWhiteSpace(form["text"]))
    {
        var inlineText = form["text"].ToString();
        var numbers = ExtractNumbers(inlineText, regex);
        totalNumbers += numbers.Count;
        items.Add(new
        {
            source = "Inline text",
            kind = "text",
            count = numbers.Count,
            numbers
        });
    }

    foreach (var file in form.Files)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        var item = new
        {
            source = file.FileName,
            kind = extension.TrimStart('.'),
            count = 0,
            numbers = Array.Empty<string>(),
            error = (string?)null
        };

        try
        {
            string content = extension switch
            {
                ".txt" or ".csv" => await ReadAsStringAsync(file),
                ".docx" => await ReadDocxAsync(file),
                ".xlsx" => await ReadWorkbookAsync(file),
                _ => throw new InvalidOperationException("Unsupported file type.")
            };

            var numbers = ExtractNumbers(content, regex);
            totalNumbers += numbers.Count;

            item = new
            {
                item.source,
                item.kind,
                count = numbers.Count,
                numbers,
                error = (string?)null
            };
        }
        catch (Exception ex)
        {
            item = new
            {
                item.source,
                item.kind,
                item.count,
                item.numbers,
                error = ex.Message
            };
        }

        items.Add(item);
    }

    return Results.Ok(new
    {
        includeDecimals,
        includeCurrencySymbols,
        ignoreNumbersInWords,
        totalNumbers,
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

static string BuildPattern(bool includeDecimals, bool includeCurrencySymbols, bool ignoreNumbersInWords)
{
    var numberCore = includeDecimals
        ? "\\d+(?:,\\d{3})*(?:\\.\\d+)?"
        : "\\d+(?:,\\d{3})*";

    var currencyPrefix = includeCurrencySymbols ? "(?:[$€£¥₹]|USD|EUR|GBP|JPY|INR)?\\s?" : string.Empty;
    var boundaryStart = ignoreNumbersInWords ? "(?<![A-Za-z])" : string.Empty;
    var boundaryEnd = ignoreNumbersInWords ? "(?![A-Za-z])" : string.Empty;

    return $"{boundaryStart}{currencyPrefix}{numberCore}{boundaryEnd}";
}

static async Task<string> ReadAsStringAsync(IFormFile file)
{
    using var reader = new StreamReader(file.OpenReadStream());
    return await reader.ReadToEndAsync();
}

static async Task<string> ReadDocxAsync(IFormFile file)
{
    await using var stream = new MemoryStream();
    await file.CopyToAsync(stream);
    stream.Seek(0, SeekOrigin.Begin);

    using var wordDoc = WordprocessingDocument.Open(stream, false);
    var body = wordDoc.MainDocumentPart?.Document.Body;
    return body?.InnerText ?? string.Empty;
}

static async Task<string> ReadWorkbookAsync(IFormFile file)
{
    await using var stream = new MemoryStream();
    await file.CopyToAsync(stream);
    stream.Seek(0, SeekOrigin.Begin);

    var sb = new StringBuilder();
    using var workbook = new XLWorkbook(stream);

    foreach (var worksheet in workbook.Worksheets)
    {
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            continue;
        }

        foreach (var cell in usedRange.CellsUsed())
        {
            var value = cell.GetFormattedString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                sb.AppendLine(value);
            }
        }
    }

    return sb.ToString();
}

static List<string> ExtractNumbers(string content, Regex regex)
{
    return regex.Matches(content)
        .Select(match => match.Value.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();
}
