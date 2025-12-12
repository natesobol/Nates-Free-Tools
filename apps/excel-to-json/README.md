# Excel to JSON Converter

A web application that converts Excel spreadsheets (.xls, .xlsx) to JSON format with preview and download capabilities.

## Structure

```
apps/excel-to-json/
├── src/
│   └── routes/
│       └── excel.js          # Express routes for Excel conversion
├── views/
│   └── excel-to-json.ejs     # Server-rendered view template
├── index.html                # Static HTML version for GitHub Pages
└── README.md
```

## Features

- Upload Excel files (.xls, .xlsx) up to 5MB
- Preview JSON output in the browser
- Download converted JSON files
- Multi-sheet support (each sheet becomes a separate JSON array)
- Server-side processing with Express/Node.js
- Static preview with client-side processing

## Usage

### Server-Side (Full Features)
The app is integrated into the main Express server at `/excel-to-json`:
- Upload files via the web interface
- Server processes the Excel file using the `xlsx` library
- Download JSON files directly

### Static Version (GitHub Pages)
The `index.html` file provides a client-side-only version that:
- Processes files entirely in the browser
- No server required
- Perfect for static hosting

## Routes

- `GET /excel-to-json` - Display the converter page
- `POST /excel-to-json` - Process uploaded Excel file
- `POST /excel-to-json/download` - Download the converted JSON file

## Dependencies

- `express` - Web framework
- `multer` - File upload handling
- `xlsx` - Excel file parsing
- `path` - File path utilities
