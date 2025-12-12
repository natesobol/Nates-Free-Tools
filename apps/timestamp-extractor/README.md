# Timestamp Extractor

A minimal ASP.NET Core 8 webapp that pulls timestamps from transcripts, logs, chat exports, and subtitle files. Upload .srt, .vtt, .txt, .csv, or .json files and filter matches by duration or frequency.

## Running locally

```bash
cd apps/timestamp-extractor
dotnet run
```

Then open http://localhost:5000 (or https://localhost:5001) to upload files, set filters, and download the results as JSON.

## Features
- Accepts `.srt`, `.vtt`, `.txt`, `.csv`, and `.json` files
- Extracts timestamp ranges (e.g., `00:01:02,500 --> 00:01:04,900`) plus standalone times (e.g., `00:12:30` or `12:30 PM`)
- Computes durations for subtitle-style ranges
- Optional filters for minimum duration (seconds) or minimum frequency of a timestamp appearing
- Returns source file, context line, and computed duration for every match
