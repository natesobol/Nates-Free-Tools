# List Structure Extractor

A C#/.NET 8 minimal API that extracts bulleted and numbered list items from uploads and annotates each item with its nesting level and the nearest heading. Supports exporting results to JSON, CSV, or flattened text.

## Running locally

1. From `apps/list-structure-extractor/`, restore dependencies and start the server:

   ```bash
   dotnet run
   ```

2. Open the web UI at the printed port (for example `http://localhost:5197`).

3. Upload one or more `.docx`, `.pdf`, `.txt`, or `.md` files and choose the desired export format.

## Features

- Detects Word list numbering, bullet characters, and nesting
- Tracks the closest heading in `.docx` files and markdown headings in `.md`/`.txt`
- Handles indentation-based nesting for plain text, markdown, and PDF text
- Exports results as JSON, CSV, or flattened text summaries
- 50 MB upload limit by default (configurable via `FormOptions`)
