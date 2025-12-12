using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;
using Xceed.Words.NET;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/questions/extract", async (
    HttpRequest request,
    int? minLength,
    int? maxLength,
    string? keywords,
    bool includeContext,
    bool exportCsv) =>
{
    var form = await request.ReadFormAsync();
    if (form.Files.Count == 0)
    {
        return Results.BadRequest("No files uploaded. Please attach at least one supported file.");
    }

    var keywordList = (keywords ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(k => k.ToLowerInvariant())
        .ToArray();

    var results = new List<QuestionResult>();

    foreach (var file in form.Files)
    {
        var text = await ExtractTextAsync(file);
        var sentences = SplitSentences(text);

        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i].Trim();
            if (!sentence.EndsWith("?"))
            {
                continue;
            }

            if (minLength.HasValue && sentence.Length < minLength.Value)
            {
                continue;
            }

            if (maxLength.HasValue && sentence.Length > maxLength.Value)
            {
                continue;
            }

            if (keywordList.Length > 0 && !keywordList.Any(k => sentence.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var contextBefore = includeContext && i > 0 ? sentences[i - 1].Trim() : null;
            var contextAfter = includeContext && i < sentences.Count - 1 ? sentences[i + 1].Trim() : null;

            results.Add(new QuestionResult
            {
                FileName = file.FileName,
                Position = i + 1,
                Question = sentence,
                ContextBefore = contextBefore,
                ContextAfter = contextAfter
            });
        }
    }

    if (exportCsv)
    {
        var csv = BuildCsv(results);
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", "questions.csv");
    }

    return Results.Ok(new QuestionExtractionResponse(results.Count, results));
})
.WithName("ExtractQuestions")
.WithOpenApi(options =>
{
    options.Summary = "Extract interrogative sentences from uploaded documents.";
    options.Description = "Uploads .txt, .csv, .docx, or .pdf files and returns only questions with optional keyword and length filters.";
    return options;
});

app.Run();

static async Task<string> ExtractTextAsync(IFormFile file)
{
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    switch (extension)
    {
        case ".txt":
        case ".csv":
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return await reader.ReadToEndAsync();
            }
        case ".docx":
            return await LoadDocxTextAsync(file);
        case ".pdf":
            return await LoadPdfTextAsync(file);
        default:
            throw new InvalidOperationException($"Unsupported file type: {extension}. Supported files are .txt, .csv, .docx, and .pdf.");
    }
}

static async Task<string> LoadDocxTextAsync(IFormFile file)
{
    var tempPath = Path.GetTempFileName() + ".docx";
    await using (var stream = File.Create(tempPath))
    {
        await file.CopyToAsync(stream);
    }

    try
    {
        using var doc = DocX.Load(tempPath);
        return doc.Text;
    }
    finally
    {
        File.Delete(tempPath);
    }
}

static async Task<string> LoadPdfTextAsync(IFormFile file)
{
    var tempPath = Path.GetTempFileName() + ".pdf";
    await using (var stream = File.Create(tempPath))
    {
        await file.CopyToAsync(stream);
    }

    try
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(tempPath);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }
    finally
    {
        File.Delete(tempPath);
    }
}

static List<string> SplitSentences(string text)
{
    var segments = Regex.Split(text, "(?<=[\\.!?])\\s+", RegexOptions.Multiline)
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .ToList();
    return segments;
}

static string BuildCsv(IEnumerable<QuestionResult> results)
{
    var builder = new StringBuilder();
    builder.AppendLine("File,Position,Question,ContextBefore,ContextAfter");

    foreach (var item in results)
    {
        builder.AppendLine(string.Join(',',
            EscapeCsv(item.FileName),
            item.Position,
            EscapeCsv(item.Question),
            EscapeCsv(item.ContextBefore ?? string.Empty),
            EscapeCsv(item.ContextAfter ?? string.Empty)));
    }

    return builder.ToString();
}

static string EscapeCsv(object value)
{
    var text = value?.ToString() ?? string.Empty;
    if (text.Contains('"'))
    {
        text = text.Replace("\"", "\"\"");
    }

    if (text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
    {
        return $"\"{text}\"";
    }

    return text;
}

record QuestionResult
{
    public required string FileName { get; init; }
    public required int Position { get; init; }
    public required string Question { get; init; }
    public string? ContextBefore { get; init; }
    public string? ContextAfter { get; init; }
}

record QuestionExtractionResponse(int Count, IReadOnlyCollection<QuestionResult> Results);
