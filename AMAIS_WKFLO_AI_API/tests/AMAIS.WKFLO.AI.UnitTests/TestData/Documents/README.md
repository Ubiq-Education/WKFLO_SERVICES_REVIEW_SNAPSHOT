# Document text extraction samples

Fixtures for `Base64ExtensionsDocumentTextTests` (chat attachment text extraction).

## `.doc` and `.xls` files

Copied unmodified from the Apache POI test-data folders
(<https://github.com/apache/poi/tree/trunk/test-data>, `document/` and `spreadsheet/`).
Licence: Apache License 2.0 (<https://www.apache.org/licenses/LICENSE-2.0>).

| File | Why it is here |
|------|----------------|
| `simple.doc` | Plain Word 97 document, one 8-bit piece |
| `SampleDoc.doc` | 8-bit piece containing Windows-1252 characters (curly apostrophe) |
| `HeaderFooterUnicode.doc` | UTF-16 pieces (symbols, accents), plus header/footer text that must not appear |
| `Bug47742.doc` | Multiple pieces mixing 8-bit and UTF-16 text |
| `test-fields.doc` | Contains fields; field codes must be dropped and results kept |
| `SampleSS.xls` | Multi-sheet workbook with text and numbers |
| `1900DateWindowing.xls` | Date cells |
| `chinese-provinces.xls` | Non-Latin (Unicode) text |

## `.pdf` files

Generated with PdfPig's `PdfDocumentBuilder` (Apache-2.0) using the standard Helvetica font:
`single-page.pdf` (one page, two lines) and `multi-page.pdf` (three pages, a heading and a body line each).

## Malformed files

`forged-dimensions.xls` is a byte-patched Apache POI `.xls` sample (Apache-2.0) whose sheet header declares
65,536 columns x 64,001 rows. It guards the work budget in `.xls` extraction (it used to burn ~15 s of CPU).
`forged-rows-single-sheet.xls` and `forged-rows-three-sheets.xls` are byte-patched Apache POI samples
(`chinese-provinces.xls` and `SampleSS.xls`) whose DIMENSIONS records declare 2 billion rows. Without the
declared-size check ExcelDataReader's final `Read()` loops over every declared row (15-43 s of CPU).
