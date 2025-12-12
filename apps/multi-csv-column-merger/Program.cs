using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression();

var app = builder.Build();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/merge", async Task<IResult> (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data with CSV uploads." });
    }

    var form = await request.ReadFormAsync();
    var joinType = form["joinType"].ToString().Equals("inner", StringComparison.OrdinalIgnoreCase) ? "inner" : "outer";
    var requestedKey = form["key"].ToString();

    var datasets = new List<CsvDataset>();

    foreach (var file in form.Files)
    {
        if (file.Length == 0)
        {
            continue;
        }

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var csv = new CsvReader(reader, config);

        try
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader())
            {
                continue;
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to read {file.FileName}.", details = ex.Message });
        }

        var headers = (csv.HeaderRecord ?? Array.Empty<string>())
            .Select(h => h.Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<Dictionary<string, string>>(capacity: 64);

        while (await csv.ReadAsync())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                var value = csv.GetField(header) ?? string.Empty;
                row[header] = value;
            }

            rows.Add(row);
        }

        datasets.Add(new CsvDataset
        {
            FileName = file.FileName,
            Headers = headers,
            Rows = rows
        });
    }

    if (datasets.Count < 2)
    {
        return Results.BadRequest(new { error = "Upload at least two CSV files to merge." });
    }

    var headerSets = datasets.Select(d => new HashSet<string>(d.Headers, StringComparer.OrdinalIgnoreCase)).ToList();
    var keyColumn = DetermineKeyColumn(requestedKey, headerSets);

    if (keyColumn is null)
    {
        return Results.BadRequest(new { error = "Could not find a shared key column. Provide one manually or ensure files share a header." });
    }

    var normalizedKey = keyColumn;

    if (!headerSets.All(set => set.Contains(normalizedKey)))
    {
        return Results.BadRequest(new { error = $"Key column '{normalizedKey}' was not found in every file." });
    }

    var keyToRowPerDataset = new List<Dictionary<string, Dictionary<string, string>>>(datasets.Count);
    foreach (var dataset in datasets)
    {
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dataset.Rows)
        {
            if (!row.TryGetValue(normalizedKey, out var keyVal) || string.IsNullOrWhiteSpace(keyVal))
            {
                continue;
            }

            map[keyVal] = row;
        }

        keyToRowPerDataset.Add(map);
    }

    var combinedHeaders = BuildCombinedHeaders(datasets, normalizedKey);

    HashSet<string> keyUniverse;
    if (joinType == "inner")
    {
        var seed = new HashSet<string>(keyToRowPerDataset.First().Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var map in keyToRowPerDataset.Skip(1))
        {
            seed.IntersectWith(map.Keys);
        }

        keyUniverse = seed;
    }
    else
    {
        keyUniverse = keyToRowPerDataset
            .SelectMany(m => m.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    var mergedRows = new List<Dictionary<string, string>>(keyUniverse.Count);

    foreach (var key in keyUniverse.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
        var mergedRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [normalizedKey] = key
        };

        foreach (var dataset in keyToRowPerDataset)
        {
            if (!dataset.TryGetValue(key, out var row))
            {
                continue;
            }

            foreach (var header in combinedHeaders)
            {
                if (header.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!mergedRow.ContainsKey(header) && row.TryGetValue(header, out var value))
                {
                    mergedRow[header] = value;
                }
                else if (mergedRow.TryGetValue(header, out var existing) && string.IsNullOrWhiteSpace(existing) && row.TryGetValue(header, out var replacement))
                {
                    mergedRow[header] = replacement;
                }
            }
        }

        mergedRows.Add(mergedRow);
    }

    var csvOutput = BuildCsv(combinedHeaders, mergedRows);

    var responseRows = mergedRows
        .Select(row => combinedHeaders.ToDictionary(h => h, h => row.TryGetValue(h, out var value) ? value : string.Empty, StringComparer.OrdinalIgnoreCase))
        .ToList();

    return Results.Ok(new
    {
        joinType,
        keyColumn = normalizedKey,
        columns = combinedHeaders,
        totalRows = responseRows.Count,
        fileSummaries = datasets.Select(d => new
        {
            d.FileName,
            headers = d.Headers,
            rows = d.Rows.Count
        }),
        csv = csvOutput,
        rows = responseRows
    });
});

app.Run();

static string? DetermineKeyColumn(string? requestedKey, List<HashSet<string>> headerSets)
{
    if (headerSets.Count == 0)
    {
        return null;
    }

    if (!string.IsNullOrWhiteSpace(requestedKey))
    {
        var candidate = headerSets[0].FirstOrDefault(h => h.Equals(requestedKey, StringComparison.OrdinalIgnoreCase));
        if (candidate is not null && headerSets.All(set => set.Contains(candidate)))
        {
            return candidate;
        }
    }

    var preferredKeys = new[]
    {
        "email", "email address", "user id", "id", "record id", "uid", "product id", "order id", "sku", "account id"
    };

    foreach (var candidate in preferredKeys)
    {
        var found = headerSets[0].FirstOrDefault(h => h.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (found is not null && headerSets.All(set => set.Contains(found)))
        {
            return found;
        }
    }

    var intersection = new HashSet<string>(headerSets[0], StringComparer.OrdinalIgnoreCase);
    foreach (var set in headerSets.Skip(1))
    {
        intersection.IntersectWith(set);
    }

    return intersection.FirstOrDefault();
}

static List<string> BuildCombinedHeaders(List<CsvDataset> datasets, string keyColumn)
{
    var headers = new List<string> { keyColumn };
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keyColumn };

    foreach (var dataset in datasets)
    {
        foreach (var header in dataset.Headers)
        {
            if (seen.Add(header))
            {
                headers.Add(header);
            }
        }
    }

    return headers;
}

static string BuildCsv(List<string> headers, List<Dictionary<string, string>> rows)
{
    var sb = new StringBuilder();
    sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));

    foreach (var row in rows)
    {
        var fields = headers.Select(header => row.TryGetValue(header, out var value) ? value : string.Empty);
        sb.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
    }

    return sb.ToString();
}

static string EscapeCsv(string? value)
{
    if (value is null)
    {
        return string.Empty;
    }

    var needsQuotes = value.Contains('\n') || value.Contains('\r') || value.Contains(',') || value.Contains('"');
    if (!needsQuotes)
    {
        return value;
    }

    var escaped = value.Replace("\"", "\"\"");
    return $"\"{escaped}\"";
}

class CsvDataset
{
    public string FileName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
}
