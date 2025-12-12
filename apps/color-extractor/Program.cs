using System.Globalization;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

var namedColors = new[]
{
    "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond", "blue",
    "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue", "cornsilk",
    "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki",
    "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen", "darkslateblue",
    "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey",
    "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod",
    "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
    "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow",
    "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
    "lightslategrey", "lightsteelblue", "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
    "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
    "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite", "navy",
    "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
    "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "red", "rosybrown",
    "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna", "silver", "skyblue",
    "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan", "teal", "thistle", "tomato",
    "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow", "yellowgreen"
};

var namedColorRegex = new Regex($"\\b(?:{string.Join("|", namedColors)})\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
var hexRegex = new Regex("#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\\b", RegexOptions.Compiled);
var rgbRegex = new Regex(
    "rgba?\\s*\\(\\s*(?<r>\\d{1,3})\\s*,\\s*(?<g>\\d{1,3})\\s*,\\s*(?<b>\\d{1,3})(?:\\s*,\\s*(?<a>0|1|0?\\.\\d+))?\\s*\\)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);

app.MapPost("/api/extract-colors", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data with files or inline text." });
    }

    var form = await request.ReadFormAsync();
    var files = form.Files;
    var inlineText = form["text"].ToString();

    if (files.Count == 0 && string.IsNullOrWhiteSpace(inlineText))
    {
        return Results.BadRequest(new { error = "Upload at least one file or paste markup to analyze." });
    }

    var sources = new List<object>();
    var palette = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(inlineText))
    {
        var colors = ExtractColors(inlineText, hexRegex, rgbRegex, namedColorRegex);
        MergePalette(palette, colors);
        sources.Add(new
        {
            source = "Inline text",
            kind = "text",
            count = colors.Values.Sum(),
            uniqueColors = colors.Count,
            colors = ToColorList(colors)
        });
    }

    foreach (var file in files)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".css" && extension != ".html" && extension != ".svg" && extension != ".json")
        {
            sources.Add(new
            {
                source = file.FileName,
                kind = string.IsNullOrWhiteSpace(extension) ? "unknown" : extension.TrimStart('.'),
                count = 0,
                uniqueColors = 0,
                colors = Array.Empty<object>(),
                error = "Unsupported file type. Use CSS, HTML, SVG, or JSON exports."
            });
            continue;
        }

        try
        {
            string content;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                content = await reader.ReadToEndAsync();
            }

            var colors = ExtractColors(content, hexRegex, rgbRegex, namedColorRegex);
            MergePalette(palette, colors);

            sources.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                count = colors.Values.Sum(),
                uniqueColors = colors.Count,
                colors = ToColorList(colors)
            });
        }
        catch (Exception ex)
        {
            sources.Add(new
            {
                source = file.FileName,
                kind = extension.TrimStart('.'),
                count = 0,
                uniqueColors = 0,
                colors = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }

    var paletteList = palette
        .OrderByDescending(kvp => kvp.Value)
        .ThenBy(kvp => kvp.Key)
        .Select(kvp => new { color = kvp.Key, occurrences = kvp.Value })
        .ToList();

    return Results.Ok(new
    {
        totalColors = palette.Values.Sum(),
        uniqueColors = palette.Count,
        palette = paletteList,
        sources
    });
});

app.Run();

static Dictionary<string, int> ExtractColors(string content, Regex hexRegex, Regex rgbRegex, Regex namedRegex)
{
    var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (Match match in hexRegex.Matches(content))
    {
        var normalized = NormalizeHex(match.Value);
        AddOrIncrement(results, normalized);
    }

    foreach (Match match in rgbRegex.Matches(content))
    {
        var normalized = NormalizeRgb(match);
        AddOrIncrement(results, normalized);
    }

    foreach (Match match in namedRegex.Matches(content))
    {
        var normalized = match.Value.ToLowerInvariant();
        AddOrIncrement(results, normalized);
    }

    return results;
}

static void AddOrIncrement(IDictionary<string, int> map, string key)
{
    if (map.TryGetValue(key, out var count))
    {
        map[key] = count + 1;
        return;
    }

    map[key] = 1;
}

static string NormalizeHex(string value)
{
    var trimmed = value.Trim();
    if (!trimmed.StartsWith('#'))
    {
        trimmed = "#" + trimmed;
    }

    var digits = trimmed[1..];
    if (digits.Length is 3 or 4)
    {
        digits = string.Concat(digits.Select(c => char.ToUpperInvariant(c)).SelectMany(c => new[] { c, c }));
    }
    else
    {
        digits = digits.ToUpperInvariant();
    }

    return "#" + digits;
}

static string NormalizeRgb(Match match)
{
    var r = ClampByte(match.Groups["r"].Value);
    var g = ClampByte(match.Groups["g"].Value);
    var b = ClampByte(match.Groups["b"].Value);
    var alphaGroup = match.Groups["a"];

    if (alphaGroup.Success)
    {
        var alpha = ParseAlpha(alphaGroup.Value);
        return $"rgba({r}, {g}, {b}, {alpha})";
    }

    return $"rgb({r}, {g}, {b})";
}

static int ClampByte(string value)
{
    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
    {
        return Math.Clamp(parsed, 0, 255);
    }

    return 0;
}

static string ParseAlpha(string value)
{
    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
    {
        var clamped = Math.Clamp(parsed, 0, 1);
        return clamped.ToString(clamped % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    return "1";
}

static void MergePalette(IDictionary<string, int> palette, IDictionary<string, int> addition)
{
    foreach (var kvp in addition)
    {
        if (palette.TryGetValue(kvp.Key, out var count))
        {
            palette[kvp.Key] = count + kvp.Value;
        }
        else
        {
            palette[kvp.Key] = kvp.Value;
        }
    }
}

static IEnumerable<object> ToColorList(Dictionary<string, int> colors)
{
    return colors
        .OrderByDescending(kvp => kvp.Value)
        .ThenBy(kvp => kvp.Key)
        .Select(kvp => new { color = kvp.Key, occurrences = kvp.Value });
}
