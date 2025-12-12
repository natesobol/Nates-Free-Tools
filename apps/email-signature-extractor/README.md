# Email Signature Extractor

A lightweight ASP.NET Core 8 static webapp that scans uploaded email files, detects signature blocks, and exports the contact details for CRM enrichment.

## Running locally

```bash
cd apps/email-signature-extractor
dotnet run
```

Then open http://localhost:5000 (or https://localhost:5001) to upload .eml, .msg, .mbox, or .txt email files, detect signatures, and export the results to CSV.

## Features
- Client-side parsing of EML, MSG, MBOX, and TXT files
- Detects signatures by common sign-offs plus phone, email, company, and title patterns
- Option to keep only one signature per conversation thread
- CSV export with Name, Email, Phone, and Company columns
- Designed for sales and CRM teams needing quick contact extraction from inbox archives
