# Formatted Text Extractor

A minimal ASP.NET Core webapp that pulls out bold, italic, underlined, strikethrough, or highlighted snippets from uploaded documents.

## Supported files
- DOCX
- PDF
- HTML
- Markdown
- TXT (basic markdown-style emphasis detection)

## Features
- Choose whether to keep styles separate or merge them into a single emphasis bucket.
- Group results by file or by heading/section when available.
- Export findings as CSV or TXT, or open a quick HTML preview.

## Running locally
```bash
cd apps/formatted-text-extractor
dotnet run
```
Then open http://localhost:5000 in your browser.
