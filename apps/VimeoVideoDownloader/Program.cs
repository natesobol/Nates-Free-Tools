using System.Net.Mime;
using VimeoVideoDownloader.Models;
using VimeoVideoDownloader.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<VimeoClient>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/download-options", async (DownloadRequest request, VimeoClient client) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "A Vimeo URL is required." });
    }

    try
    {
        var options = await client.GetDownloadOptionsAsync(request);
        return Results.Ok(options);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/download", async (DownloadRequest request, VimeoClient client, HttpResponse response) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "A Vimeo URL is required." });
    }

    try
    {
        var selection = await client.SelectFileAsync(request);
        if (selection is null)
        {
            return Results.NotFound(new { error = "No matching download options were found for this video." });
        }

        var stream = await client.GetVideoStreamAsync(selection.Url);
        var fileName = client.BuildFileName(selection, request.Url);

        response.Headers.CacheControl = "no-store";
        return Results.Stream(stream, selection.MimeType ?? MediaTypeNames.Application.Octet, fileDownloadName: fileName);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
