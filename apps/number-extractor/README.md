# Number Extractor

A minimal ASP.NET Core web app that extracts numeric values from inline text plus TXT, CSV, DOCX, and XLSX uploads. Toggle whether to allow decimals or currency symbols and ignore numbers embedded in words like `ModelX500`.

## Endpoints
- `GET /health` - health check.
- `POST /api/extract` - multipart form endpoint that accepts `files` and optional `text` plus flags `includeDecimals`, `includeCurrencySymbols`, and `ignoreNumbersInWords`.

## Running locally
```bash
cd apps/number-extractor
DOTNET_URLS=http://localhost:5106 dotnet run
```
Then open http://localhost:5106 to use the UI.
