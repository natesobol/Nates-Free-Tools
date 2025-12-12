# Color Extractor

A minimal ASP.NET Core web app that scans CSS, HTML, SVG, and JSON design exports for color values. It detects hex values, `rgb()`/`rgba()`, and CSS named colors, then reports per-file counts plus an aggregated palette.

## Endpoints
- `POST /api/extract-colors` – multipart form endpoint that accepts `files` plus optional `text` for inline snippets. Only `.css`, `.html`, `.svg`, and `.json` files are processed.

## Running locally
```bash
cd apps/color-extractor
DOTNET_URLS=http://localhost:5115 dotnet run
```
Then open http://localhost:5115 to use the UI.
