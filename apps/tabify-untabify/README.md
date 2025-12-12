# Tabify or Untabify

A lightweight C# minimal API that converts tabs to spaces or spaces to tabs for quick code formatting fixes. Paste text, choose your direction, set spaces-per-tab, and preview the cleaned result instantly.

## Running locally

```
dotnet run --project tabify-untabify.csproj
```

Open the URL shown in the console (usually `http://localhost:5000`) to use the text-only interface served from `wwwroot`.

## API

`POST /api/convert`

- **Body (JSON)**
  - `text` (string, required): The text to transform.
  - `mode` (string, optional): `"tabs-to-spaces"` (default) or `"spaces-to-tabs"`.
  - `spacesPerTab` (number, optional): How many spaces equal one tab. Clamped between 1 and 8, defaults to 4.
  - `trimTrailingWhitespace` (bool, optional): Remove trailing spaces/tabs before conversion. Defaults to `true`.

### Example request

```bash
curl -X POST http://localhost:5000/api/convert \
  -H "Content-Type: application/json" \
  -d '{"text":"\tIndented line","mode":"tabs-to-spaces","spacesPerTab":2}'
```

### Example response

```json
{
  "mode": "TabsToSpaces",
  "trimTrailingWhitespace": true,
  "stats": {
    "tabsToSpaces": true,
    "spacesPerTab": 2,
    "inputLength": 14,
    "outputLength": 15,
    "tabsFound": 1,
    "spaceRunsConverted": 0
  },
  "converted": "  Indented line",
  "preview": "  Indented line"
}
```
