# Milestone 40: the layout model as the document-to-Markdown converter

What was found on 2026-11-27 about Azure Document Intelligence's layout model (`prebuilt-layout`),
which the inputs group chose for turning PDFs and scanned pages into Markdown
([`input-kinds.md`](input-kinds.md) § The converter). The investigation that follows from it is
[#1538](https://github.com/Consultologist/Consultologist-Blazor/issues/1538).

Everything here was read from Microsoft's documentation. Nothing was run.

## What was found

- **Its Markdown is not Markdig's.** At the current API version (2024-11-30) it writes tables as
  HTML tables, to keep merged cells, and writes page headers, footers, numbers and breaks as HTML
  comments. With raw HTML disabled, all of that would show as typed characters, so the converter
  cannot pass its output through.
- **It costs more.** Microsoft's Q&A answers put it at about US$10 for 1,000 pages against about
  US$1.50 for today's `prebuilt-read`, with the read included in the price. This is not confirmed
  on the pricing page or for the Canadian region.
- **It returns the same content as structured data.** Paragraphs carry roles (title, section
  heading, page header, footer), and tables carry cells with row and column spans, alongside the
  Markdown.
- **It reads DOCX too,** extracting the embedded text as it is; embedded images are not supported.
- **Not confirmed:** that Markdown output is available in the Canadian region the engine uses.

## Sources

| Fact | Source |
|---|---|
| Markdown output with `outputContentFormat=markdown`; tables as HTML tables at 2024-11-30; paragraph roles; table cells with spans; DOCX read as embedded text | [What is the Document Intelligence layout model?](https://learn.microsoft.com/azure/ai-services/document-intelligence/prebuilt/layout?view=doc-intel-4.0.0) |
| Page headers, footers, numbers and breaks as HTML comments | [Understanding Document Intelligence Layout API Markdown output format](https://learn.microsoft.com/azure/ai-services/document-intelligence/concept/markdown-elements?view=doc-intel-4.0.0) |
| About US$10 against US$1.50 for 1,000 pages, the read included | Microsoft Q&A answers [1896086](https://learn.microsoft.com/answers/a/1896086) and [12439288](https://learn.microsoft.com/answers/a/12439288) |
| Limits: 2,000 pages and 500 MB for a PDF on the paid tier; a password-locked PDF must be unlocked first | the layout model page, § Input requirements |
| The engine calls `prebuilt-read` through `Azure.AI.DocumentIntelligence` 1.0.0 | `AzureDocumentIntelligenceOcr.cs` and `Directory.Packages.props` on `m39-build` at `94b66aec` |

## What the owner has decided around it

- **The service, or an off-the-shelf component, is preferred over custom code.** Whether the
  Markdown is built from the service's Markdown or from its structured result is not decided; #1538
  finds the smallest step between the service's output and the engine's Markdown.
- **A table with no spans is written as a pipe table, and one with spans as a grid table,** for DOCX
  and PDF alike.
- **Page headers, footers and page numbers are dropped** from the Markdown, and page breaks too. The
  extraction's record row says how many were dropped.
- **The higher price is accepted.** A `plaintext` extraction keeps using `prebuilt-read`, so an
  author who does not need structure does not pay for it.
- **A PDF with a text layer** goes to the layout model for `markdown_block`, since its text layer
  has no headings or tables to read; for `plaintext` it is parsed locally, as today.

## To investigate (#1538)

In two halves. The reading half (documentation, pricing, regions, which libraries exist) is done in
the next planning session. The measuring half (calls to the service and throwaway scripts, on
fictional documents) waits for Milestone 40's building to start, as the first task of the converter
slice.

- The smallest step from the service's output to the engine's Markdown: its Markdown with the HTML
  tables rewritten and the comments removed, by an off-the-shelf library if one keeps spans; or
  Markdown built from the structured result. Measured on fictional documents.
- Whether an option or an API version writes Markdown tables in place of HTML ones.
- DOCX: through the layout model as well, through an off-the-shelf library, or the engine's own
  deterministic code. Own code is chosen only if neither of the others keeps headings, lists and
  tables or gives the same bytes on each run.
- The price on the pricing page for the engine's region, and how a DOCX's pages are counted.
- Markdown output and API version 2024-11-30 in the Canadian region.
- Whether the same file gives the same output over time, since the output is hashed.
