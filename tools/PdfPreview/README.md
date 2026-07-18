# PdfPreview — Phase 8 template drafting

Draft and preview the QuestPDF document templates (job sheets, invoices, …) with realistic sample data,
before they are wired into the API. Once a format is approved, the template class (e.g.
`JobSheetDocument`) moves into `Smartnet.Infrastructure/Pdf` and the API renders it from a document snapshot.

## Render to a file (no extra tools)

```bash
dotnet run --project tools/PdfPreview
```

Writes `tools/PdfPreview/out/jobsheet-st.pdf` and a PNG per page (`jobsheet-st-0.png`, …). Open either to
review the layout.

## Live preview in the QuestPDF Companion (hot-reload)

The Companion is a desktop app that shows the document and refreshes as you edit the template.

1. Install it once:
   ```bash
   dotnet tool install --global QuestPDF.Companion
   ```
2. Launch it (`questpdf-companion`, or from your app menu) and leave it open.
3. Run the preview in watch mode:
   ```bash
   dotnet watch --project tools/PdfPreview run -- --companion
   ```
   Save a change to `JobSheetDocument.cs` and the Companion updates live.

## Current templates

- **`JobSheetDocument`** — Smart Technologies (Job_ST) job sheet. Sample data from job STJ-5.

Company header contact details are placeholders: `companies_m` holds only the name today, so the address /
phone / email / logo will come from Settings when the template is wired to real data.
