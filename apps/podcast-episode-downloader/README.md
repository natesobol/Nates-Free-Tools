# Podcast Episode Downloader

A minimal ASP.NET Core webapp for downloading podcast episodes directly from public RSS feeds or individual episode URLs. The
frontend lists episode metadata and lets users grab one or many files as MP3s; multiple selections are bundled as a ZIP for
easy archiving.

## Running locally

```bash
cd apps/podcast-episode-downloader
DOTNET_ENVIRONMENT=Development dotnet run
```

Then open http://localhost:5000 to use the web UI.

## API

- `POST /api/fetch-episodes` – Body: `{ "url": "<feed or episode url>" }`. Returns episode metadata with title, published
date, duration (if provided by the feed), and the audio download URL.
- `POST /api/download` – Body: `{ "episodes": [{ "url": "...", "title": "optional" }], "bundleAsZip": true }`. Downloads a
single episode or bundles multiple selections into a ZIP archive.

## Notes

- The app tries to parse standard RSS/iTunes podcast feeds using `System.ServiceModel.Syndication` and falls back to direct
audio downloads when pointed to an individual episode file.
- File names are sanitized for safe zipping, and missing extensions default to `.mp3`.
- Response compression is enabled to keep payload sizes light.
