# Image Path Extractor

Drop in `.html`, `.md`, or `.txt` files and the webapp will list every image path it finds. It checks for HTML `<img>` tags, Markdown image syntax, and plain text file paths ending in common image extensions. Results include the source file name, where the match was found, and the line number for quick cross-referencing.

## Running locally

```bash
dotnet run --project image-path-extractor.csproj
```

Open the printed URL (defaults to `http://localhost:5000`) to use the drag-and-drop UI hosted from `wwwroot`.
