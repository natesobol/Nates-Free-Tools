# Hashtag & Mention Extractor

A lightweight ASP.NET Core webapp that accepts social exports and pulls every hashtag and @mention with frequency counts.

## Features
- Upload .txt, .csv, or .json exports or paste inline text.
- Toggle case-sensitive matching and optional deduplication for exports.
- Returns per-input results plus aggregate frequency tables for hashtags and mentions.
- Ships with a minimal HTML frontend in `wwwroot` that calls the `/api/extract` endpoint.

## Running locally
```bash
dotnet restore
dotnet run --project apps/hashtag-mention-extractor/hashtag-mention-extractor.csproj
```

Navigate to `http://localhost:5000` and start uploading or pasting content.
