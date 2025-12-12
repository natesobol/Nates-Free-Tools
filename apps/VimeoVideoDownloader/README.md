# Vimeo Video Downloader

A lightweight ASP.NET Core web app for downloading public Vimeo videos with optional password support and resolution filtering.

## Features
- Paste any Vimeo URL (supports public share links).
- Optional password field for protected clips.
- Filter by preferred resolution and container (MP4 or MOV).
- Browse available progressive download options before starting the download.

## Getting started
1. Install the .NET 8 SDK.
2. From `apps/VimeoVideoDownloader`, run:
   ```bash
   dotnet restore
   dotnet run
   ```
3. Open `http://localhost:5000` (or the port shown in the console) and paste a Vimeo URL.

> Use responsibly—ensure you have rights to download the media you access.
