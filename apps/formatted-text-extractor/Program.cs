using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using Markdig;
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

app.MapPost("/api/extract", async Task<IResult> (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data with file uploads." });
    }

    var form = await request.ReadFormAsync();
    var files = form.Files;
    var mergeStyles = bool.TryParse(form["mergeStyles"], out var merge) && merge;
    var groupBy = form["groupBy"].ToString().Equals("heading", StringComparison.OrdinalIgnoreCase)
        ? Grouping.Heading
        : Grouping.File;

    if (files.Count == 0)
    {
        return Results.BadRequest(new { error = "Upload at least one .docx, .pdf, .html, .md, or .txt file." });
    }

    var allEntries = new List<FormattedSegment>();
    var summaries = new List<FileSummary>();

    foreach (var file in files)
    {
        try
        {
            var entries = await ExtractFormattedTextAsync(file, groupBy);
            allEntries.AddRange(entries);
            summaries.Add(new FileSummary(file.FileName, entries.Count));
        }
        catch (Exception ex)
        {
            summaries.Add(new FileSummary(file.FileName, 0, ex.Message));
        }
    }

    if (mergeStyles)
    {
        allEntries = allEntries
            .Select(entry => entry with
            {
                Styles = entry.Styles.Count > 0 ? new List<string> { "Emphasis" } : entry.Styles
            })
            .ToList();
    }

    var groupedCounts = allEntries
        .GroupBy(e => e.GroupLabel)
        .Select(g => new GroupSummary(g.Key, g.Count()))
        .ToList();

    var totals = new
    {
        total = allEntries.Count,
        byStyle = allEntries
            .SelectMany(e => e.Styles.Select(style => new { style, e.Text }))
            .GroupBy(x => x.style)
            .Select(g => new { style = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList()
    };

    return Results.Ok(new
    {
        entries = allEntries,
        files = summaries,
        groups = groupedCounts,
        totals
    });
});

app.Run();

static async Task<List<FormattedSegment>> ExtractFormattedTextAsync(IFormFile file, Grouping grouping)
{
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    await using var memory = new MemoryStream();
    await file.CopyToAsync(memory);
    memory.Position = 0;

    return extension switch
    {
        ".docx" => ExtractFromDocx(memory, file.FileName, grouping),
        ".pdf" => ExtractFromPdf(memory, file.FileName, grouping),
        ".html" or ".htm" => await ExtractFromHtmlAsync(memory, file.FileName, grouping),
        ".md" or ".markdown" => await ExtractFromMarkdownAsync(memory, file.FileName, grouping),
        ".txt" => await ExtractFromPlainTextAsync(memory, file.FileName, grouping),
        _ => throw new InvalidOperationException("Unsupported file type. Upload .docx, .pdf, .html, .md, or .txt files."),
    };
}

static List<FormattedSegment> ExtractFromDocx(Stream stream, string fileName, Grouping grouping)
{
    using var document = WordprocessingDocument.Open(stream, false);
    var body = document.MainDocumentPart?.Document.Body;
    var results = new List<FormattedSegment>();
    var currentHeading = string.Empty;

    if (body is null)
    {
        return results;
    }

    foreach (var paragraph in body.Elements<Paragraph>())
    {
        var paragraphText = string.Join(string.Empty, paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
        if (IsHeading(paragraph))
        {
            currentHeading = paragraphText;
        }

        foreach (var run in paragraph.Elements<Run>())
        {
            var text = string.Join(string.Empty, run.Descendants<Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var styles = GetDocxStyles(run);
            if (styles.Count == 0)
            {
                continue;
            }

            results.Add(new FormattedSegment(text, styles, fileName, ResolveGroupLabel(grouping, fileName, currentHeading)));
        }
    }

    return results;
}

static List<FormattedSegment> ExtractFromPdf(Stream stream, string fileName, Grouping grouping)
{
    var results = new List<FormattedSegment>();

    using var pdf = PdfDocument.Open(stream);

    foreach (var page in pdf.GetPages())
    {
        var currentStyles = new List<string>();
        var builder = new StringBuilder();

        foreach (var letter in page.Letters)
        {
            var letterStyles = StylesFromFont(letter.FontName);

            if (char.IsWhiteSpace(letter.Value[0]))
            {
                FlushCurrent();
                continue;
            }

            if (!StylesMatch(currentStyles, letterStyles))
            {
                FlushCurrent();
                currentStyles = letterStyles;
            }

            builder.Append(letter.Value);
        }

        FlushCurrent();

        void FlushCurrent()
        {
            var text = builder.ToString().Trim();
            if (text.Length > 0 && currentStyles.Count > 0)
            {
                results.Add(new FormattedSegment(text, new List<string>(currentStyles), fileName, ResolveGroupLabel(grouping, fileName, null)));
            }
            builder.Clear();
        }
    }

    return results;
}

static async Task<List<FormattedSegment>> ExtractFromHtmlAsync(Stream stream, string fileName, Grouping grouping)
{
    using var reader = new StreamReader(stream);
    var html = await reader.ReadToEndAsync();
    var document = new HtmlDocument();
    document.LoadHtml(html);

    return ExtractFromHtmlDocument(document, fileName, grouping);
}

static async Task<List<FormattedSegment>> ExtractFromMarkdownAsync(Stream stream, string fileName, Grouping grouping)
{
    using var reader = new StreamReader(stream);
    var markdown = await reader.ReadToEndAsync();
    var html = Markdig.Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
    var document = new HtmlDocument();
    document.LoadHtml(html);

    return ExtractFromHtmlDocument(document, fileName, grouping);
}

static async Task<List<FormattedSegment>> ExtractFromPlainTextAsync(Stream stream, string fileName, Grouping grouping)
{
    using var reader = new StreamReader(stream);
    var content = await reader.ReadToEndAsync();
    var results = new List<FormattedSegment>();
    var matches = Regex.Matches(content, "(\".*?\"|\*\*.*?\*\*|__.*?__|\*.*?\*|_.+?_|~~.*?~~)");

    foreach (Match match in matches)
    {
        var text = match.Value.Trim().Trim('*', '_', '~', '"');
        if (string.IsNullOrWhiteSpace(text))
        {
            continue;
        }

        var styles = new List<string>();
        var value = match.Value;
        if (value.StartsWith("**") || value.StartsWith("__")) styles.Add("Bold");
        if ((value.StartsWith("*") && !value.StartsWith("**")) || (value.StartsWith("_") && !value.StartsWith("__")))
        {
            styles.Add("Italic");
        }
        if (value.StartsWith("~~")) styles.Add("Strikethrough");

        if (styles.Count > 0)
        {
            results.Add(new FormattedSegment(text, styles.Distinct().ToList(), fileName, ResolveGroupLabel(grouping, fileName, null)));
        }
    }

    return results;
}

static List<FormattedSegment> ExtractFromHtmlDocument(HtmlDocument document, string fileName, Grouping grouping)
{
    var results = new List<FormattedSegment>();
    var currentHeading = string.Empty;

    foreach (var node in document.DocumentNode.DescendantsAndSelf())
    {
        if (IsHeading(node))
        {
            currentHeading = HtmlEntity.DeEntitize(node.InnerText.Trim());
            continue;
        }

        if (HasFormatting(node))
        {
            var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            var styles = GetHtmlStyles(node);

            if (!string.IsNullOrWhiteSpace(text) && styles.Count > 0)
            {
                results.Add(new FormattedSegment(text, styles, fileName, ResolveGroupLabel(grouping, fileName, currentHeading)));
            }
        }
    }

    return results;
}

static List<string> GetDocxStyles(Run run)
{
    var styles = new List<string>();
    var props = run.RunProperties;

    if (props == null)
    {
        return styles;
    }

    if (IsTruthy(props.Bold)) styles.Add("Bold");
    if (IsTruthy(props.Italic)) styles.Add("Italic");
    if (props.Underline?.Val is not null && props.Underline.Val != UnderlineValues.None) styles.Add("Underline");
    if (IsTruthy(props.Strike)) styles.Add("Strikethrough");
    if (props.Highlight is not null) styles.Add("Highlight");

    return styles;
}

static bool IsTruthy(OnOffType? flag)
{
    if (flag is null)
    {
        return false;
    }

    return flag.Val is null || flag.Val.Value;
}

static List<string> StylesFromFont(string? fontName)
{
    var styles = new List<string>();
    if (string.IsNullOrWhiteSpace(fontName))
    {
        return styles;
    }

    var normalized = fontName.ToLowerInvariant();
    if (normalized.Contains("bold") || normalized.Contains("black") || normalized.Contains("heavy"))
    {
        styles.Add("Bold");
    }

    if (normalized.Contains("italic") || normalized.Contains("oblique"))
    {
        styles.Add("Italic");
    }

    return styles;
}

static bool StylesMatch(List<string> current, List<string> next)
{
    if (current.Count != next.Count)
    {
        return false;
    }

    return !current.Except(next).Any();
}

static bool IsHeading(Paragraph paragraph)
{
    var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
    return styleId != null && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
}

static bool IsHeading(HtmlNode node)
{
    return node.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
}

static bool HasFormatting(HtmlNode node)
{
    if (node.NodeType != HtmlNodeType.Element)
    {
        return false;
    }

    var name = node.Name.ToLowerInvariant();
    if (name is "b" or "strong" or "i" or "em" or "u" or "s" or "strike" or "del")
    {
        return true;
    }

    var style = node.GetAttributeValue("style", string.Empty).ToLowerInvariant();
    return style.Contains("font-weight:bold") || style.Contains("font-style:italic") || style.Contains("text-decoration:underline") || style.Contains("line-through");
}

static List<string> GetHtmlStyles(HtmlNode node)
{
    var styles = new List<string>();
    var current = node;
    while (current != null && current.NodeType == HtmlNodeType.Element)
    {
        var name = current.Name.ToLowerInvariant();
        if (name is "b" or "strong") styles.Add("Bold");
        if (name is "i" or "em") styles.Add("Italic");
        if (name is "u") styles.Add("Underline");
        if (name is "s" or "del" or "strike") styles.Add("Strikethrough");

        var style = current.GetAttributeValue("style", string.Empty).ToLowerInvariant();
        if (style.Contains("font-weight:bold") && !styles.Contains("Bold")) styles.Add("Bold");
        if (style.Contains("font-style:italic") && !styles.Contains("Italic")) styles.Add("Italic");
        if (style.Contains("text-decoration:underline") && !styles.Contains("Underline")) styles.Add("Underline");
        if (style.Contains("line-through") && !styles.Contains("Strikethrough")) styles.Add("Strikethrough");

        current = current.ParentNode;
    }

    return styles.Distinct().ToList();
}

static string ResolveGroupLabel(Grouping grouping, string fileName, string? heading)
{
    return grouping == Grouping.Heading && !string.IsNullOrWhiteSpace(heading)
        ? heading
        : fileName;
}

record FormattedSegment(string Text, List<string> Styles, string FileName, string GroupLabel);
record FileSummary(string FileName, int Count, string? Error = null);
record GroupSummary(string Label, int Count);

enum Grouping
{
    File,
    Heading
}
