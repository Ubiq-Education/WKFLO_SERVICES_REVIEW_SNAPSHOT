using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelDataReader;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    public static class Base64Extensions
    {
        /// <summary>
        /// Detects the MIME type from a string containing either a data URI or base64-encoded file data.
        /// </summary>
        /// <remarks>If the input is a data URI, the MIME type is extracted from the URI header. For other
        /// base64-encoded content, the method attempts to infer the MIME type from the data. If detection fails, the
        /// generic MIME type "application/octet-stream" is returned.</remarks>
        /// <param name="fileData">The string containing the file data to analyze. This can be a data URI (e.g., "data:image/png;base64,...")
        /// or a base64-encoded file content. If the value is null, empty, or whitespace, a default MIME type is
        /// returned.</param>
        /// <returns>A string representing the detected MIME type, such as "image/png". Returns "application/octet-stream" if the
        /// MIME type cannot be determined.</returns>
        public static string DetectMimeType(this string fileData)
        {
            if (string.IsNullOrWhiteSpace(fileData))
                return "application/octet-stream";

            if (fileData.StartsWith("data:"))
            {
                // Es un data URI, extraer tipo MIME del header
                var headerEnd = fileData.IndexOf(',');
                if (headerEnd > 0)
                {
                    var header = fileData.Substring(0, headerEnd); // "data:image/png;base64"
                    var mimeTypePart = header.Split(';')[0]; // "data:image/png"
                    return mimeTypePart.Replace("data:", ""); // "image/png"
                }
            }

            // Si no es data URI, intentar detectar por contenido base64
            return DetectMimeTypeFromBase64Content(fileData);
        }


        /// <summary>
        /// Determines whether the specified MIME type represents an image format.
        /// </summary>
        /// <param name="mimeType">The MIME type string to evaluate. Can be null.</param>
        /// <returns>true if the MIME type starts with "image/"; otherwise, false.</returns>
        public static bool IsImageMimeType(this string mimeType)
        {
            return mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Determines whether the specified MIME type represents a common document format.
        /// </summary>
        /// <remarks>Recognized document formats include plain text, PDF, Microsoft Office documents, CSV,
        /// RTF, JSON, and XML. The comparison is case-insensitive.</remarks>
        /// <param name="mimeType">The MIME type string to evaluate. Can be null or empty.</param>
        /// <returns>true if the MIME type corresponds to a recognized document format; otherwise, false.</returns>
        public static bool IsDocumentMimeType(this string mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
                return false;

            var documentTypes = new[]
            {
                "text/plain",
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-powerpoint",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "text/csv",
                "application/rtf",
                "application/json",
                "application/xml",
                "text/xml"
            };

            return documentTypes.Contains(mimeType.ToLowerInvariant());
        }

        /// <summary>
        /// Detects the MIME type of data encoded as a Base64 string by inspecting its initial bytes.
        /// </summary>
        /// <remarks>The detection is based on common file signatures (magic numbers) found in the initial
        /// bytes of the decoded data. If the content does not match any known signatures or if decoding fails, the
        /// method returns "application/octet-stream" as a generic binary type. This method does not guarantee detection
        /// of all possible file types.</remarks>
        /// <param name="base64Data">A string containing the Base64-encoded data to analyze. Must not be null or empty.</param>
        /// <returns>A string representing the detected MIME type, such as "application/pdf", "image/png", or
        /// "application/octet-stream" if the type cannot be determined.</returns>
        private static string DetectMimeTypeFromBase64Content(string base64Data)
        {
            try
            {
                // Convertir una pequeña parte del base64 para detectar el tipo
                var bytes = Convert.FromBase64String(base64Data.Substring(0, Math.Min(100, base64Data.Length)));

                // Detectar por magic numbers (primeros bytes del archivo)
                if (bytes.Length >= 4)
                {
                    // PDF: %PDF
                    if (bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
                        return "application/pdf";

                    // PNG: 89 50 4E 47
                    if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                        return "image/png";

                    // JPEG: FF D8
                    if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                        return "image/jpeg";

                    // GIF: GIF8
                    if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                        return "image/gif";

                    // ZIP/DOCX/XLSX: PK (need more sophisticated detection for Office documents)
                    if (bytes[0] == 0x50 && bytes[1] == 0x4B)
                    {
                        // Try to detect if it's an Office document by looking for specific patterns
                        if (base64Data.Length > 200)
                        {
                            var moreBytes = Convert.FromBase64String(base64Data.Substring(0, Math.Min(200, base64Data.Length)));
                            var content = System.Text.Encoding.ASCII.GetString(moreBytes);

                            if (content.Contains("xl/"))
                                return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                            if (content.Contains("word/"))
                                return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                            if (content.Contains("ppt/"))
                                return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                        }
                        return "application/zip"; // Generic ZIP if Office document detection fails
                    }

                    // Legacy Excel (XLS): D0 CF 11 E0 A1 B1 1A E1 (OLE2 signature)
                    if (bytes.Length >= 8 &&
                        bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0 &&
                        bytes[4] == 0xA1 && bytes[5] == 0xB1 && bytes[6] == 0x1A && bytes[7] == 0xE1)
                        return "application/vnd.ms-excel";
                }
            }
            catch (FormatException)
            {
                // Malformed/truncated base64 during best-effort mime sniffing.
                return "application/octet-stream";
            }

            return "application/octet-stream";
        }

        /// <summary>
        /// Strips the data URI prefix from image data and returns the raw base64 string and MIME type,
        /// without decoding to bytes. Use this when you intend to pass the data to UploadBase64Async,
        /// which handles the decode internally — avoiding a double-decode cycle.
        /// </summary>
        /// <param name="fileData">A data URI (e.g. "data:image/png;base64,...") or raw base64 string.</param>
        /// <returns>The raw base64 string (no header) and the detected MIME type.</returns>
        public static (string rawBase64, string mimeType) ParseRawBase64Data(this string fileData)
        {
            if (string.IsNullOrWhiteSpace(fileData))
                throw new ArgumentNullException(nameof(fileData));

            if (fileData.StartsWith("data:"))
            {
                var headerEnd = fileData.IndexOf(',');
                if (headerEnd <= 0)
                    throw new ArgumentException("Invalid data URI format", nameof(fileData));

                var header = fileData.Substring(0, headerEnd); // "data:image/png;base64"
                var mimeType = header.Split(';')[0].Replace("data:", ""); // "image/png"
                var rawBase64 = fileData.Substring(headerEnd + 1);
                return (rawBase64, mimeType);
            }

            // Raw base64 with no data URI prefix — detect MIME from content
            return (fileData, fileData.DetectMimeType());
        }

        /// <summary>
        /// Same MIME detection as <see cref="ParseRawBase64Data"/> but never copies the payload: for a data URI
        /// only the (header-sized) prefix is read, for raw base64 only the first characters are sniffed.
        /// Use on the send path where the attachment can be tens of MB. Throws the same
        /// <see cref="ArgumentException"/> for a malformed data URI.
        /// </summary>
        /// <param name="fileData">A data URI or raw base64 string.</param>
        /// <param name="payloadOffset">Index of the first base64 character (0 for raw base64).</param>
        public static string GetMimeTypeWithoutCopy(this string fileData, out int payloadOffset)
        {
            if (string.IsNullOrWhiteSpace(fileData))
                throw new ArgumentNullException(nameof(fileData));

            if (fileData.StartsWith("data:"))
            {
                var headerEnd = fileData.IndexOf(',');
                if (headerEnd <= 0)
                    throw new ArgumentException("Invalid data URI format", nameof(fileData));

                payloadOffset = headerEnd + 1;
                var header = fileData.Substring(0, headerEnd); // "data:image/png;base64"
                return header.Split(';')[0].Replace("data:", ""); // "image/png"
            }

            payloadOffset = 0;
            return fileData.DetectMimeType(); // sniffs only the first 100 chars
        }

        /// <summary>
        /// Recognized by <see cref="DetectImageFormatFromBytes"/> via magic-number sniffing.
        /// Png/Jpeg/Gif/WebP are the formats Anthropic's Messages API actually accepts — used by
        /// <see cref="ParseImageData"/> to self-correct a mismatched declared MIME type (e.g. a data
        /// URI header claiming "image/png" for bytes that are actually a JPEG, which Anthropic
        /// rejects outright). Heic/Bmp/Tiff are recognized purely so <see cref="IsUnsupportedImageFormat"/>
        /// can reject them early with a clear message — none of our providers can use them regardless
        /// of what MIME type we declare.
        /// </summary>
        private enum DetectedImageFormat
        {
            Unknown,
            Png,
            Jpeg,
            Gif,
            WebP,
            Heic,
            Bmp,
            Tiff
        }

        private static DetectedImageFormat DetectImageFormatFromBytes(byte[] bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return DetectedImageFormat.Png;

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return DetectedImageFormat.Jpeg;

            if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return DetectedImageFormat.Gif;

            // WebP: "RIFF" at 0-3, size at 4-7, "WEBP" at 8-11
            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return DetectedImageFormat.WebP;

            // BMP: "BM"
            if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
                return DetectedImageFormat.Bmp;

            // TIFF: little-endian "II*\0" or big-endian "MM\0*"
            if (bytes.Length >= 4 &&
                ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                 (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
                return DetectedImageFormat.Tiff;

            // HEIC/HEIF: ISO-BMFF "ftyp" box at offset 4, brand at offset 8
            if (bytes.Length >= 12 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
            {
                var brand = Encoding.ASCII.GetString(bytes, 8, 4);
                if (brand is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" or "hevm" or "hevs" or "mif1" or "msf1")
                    return DetectedImageFormat.Heic;
            }

            return DetectedImageFormat.Unknown;
        }

        private static string ToMimeType(DetectedImageFormat format) => format switch
        {
            DetectedImageFormat.Png => "image/png",
            DetectedImageFormat.Jpeg => "image/jpeg",
            DetectedImageFormat.Gif => "image/gif",
            DetectedImageFormat.WebP => "image/webp",
            _ => null
        };

        /// <summary>
        /// Decodes just enough of the base64 payload to sniff the first ~16 bytes, without decoding
        /// the entire (potentially huge) image just to check a magic number.
        /// </summary>
        private static byte[] DecodePrefixBytes(string base64, int minBytes, int offset = 0)
        {
            if (string.IsNullOrEmpty(base64) || offset >= base64.Length)
                return [];

            var neededChars = ((minBytes * 4 / 3) + 3) / 4 * 4 + 4; // round up + one extra block for safety
            var length = Math.Min(neededChars, base64.Length - offset);
            length -= length % 4;
            if (length <= 0)
                return [];

            try
            {
                return Convert.FromBase64String(base64.Substring(offset, length));
            }
            catch (FormatException)
            {
                return [];
            }
        }

        /// <summary>
        /// Decodes the base64 payload that starts at <paramref name="offset"/> straight from the string. A data
        /// URI used to be decoded via Substring, which copied the whole payload first (31 MB for an 11 MB file).
        /// Throws <see cref="FormatException"/> for invalid base64, like <see cref="Convert.FromBase64String"/>.
        /// </summary>
        private static byte[] DecodeBase64(string base64, int offset)
        {
            var chars = base64.AsSpan(offset);
            var padding = chars.EndsWith("==") ? 2 : chars.EndsWith("=") ? 1 : 0;
            // Exact size for well-formed input; whitespace (allowed, as in FromBase64String) only makes it smaller.
            var bytes = new byte[Math.Max(0, (chars.Length + 3) / 4 * 3 - padding)];
            if (!Convert.TryFromBase64Chars(chars, bytes, out var written))
                throw new FormatException("The input is not a valid Base-64 string.");

            return written == bytes.Length ? bytes : bytes[..written];
        }

        /// <summary>
        /// True when the attachment's actual bytes are a recognized image format that no configured
        /// AI provider can process (HEIC/HEIF, BMP, TIFF) — most commonly a photo straight off an
        /// iPhone camera. Checked before queuing so it fails fast with a clear message instead of a
        /// confusing rejection deep inside the model call. Formats we don't recognize at all are left
        /// alone (not flagged as unsupported) since we can't be sure.
        /// </summary>
        public static bool IsUnsupportedImageFormat(this string fileData)
        {
            if (string.IsNullOrWhiteSpace(fileData))
                return false;

            // No payload copy: only the first few base64 characters after the header are decoded.
            _ = fileData.GetMimeTypeWithoutCopy(out var payloadOffset);
            var format = DetectImageFormatFromBytes(DecodePrefixBytes(fileData, 16, payloadOffset));
            return format is DetectedImageFormat.Heic or DetectedImageFormat.Bmp or DetectedImageFormat.Tiff;
        }

        public static (byte[] bytes, string mimeType) ParseImageData(this string imageData)
        {
            if (string.IsNullOrWhiteSpace(imageData))
                throw new ArgumentNullException(nameof(imageData));

            string mimeType = "image/jpeg";
            byte[] imageBytes;

            if (imageData.StartsWith("data:"))
            {
                var headerEnd = imageData.IndexOf(',');
                if (headerEnd <= 0)
                    throw new ArgumentException("Invalid data URI format", nameof(imageData));

                var header = imageData.Substring(0, headerEnd); // "data:image/png;base64"
                var mimeTypePart = header.Split(';')[0]; // "data:image/png"
                mimeType = mimeTypePart.Replace("data:", ""); // "image/png"

                imageBytes = DecodeBase64(imageData, headerEnd + 1);
            }
            else
            {
                imageBytes = Convert.FromBase64String(imageData);
            }

            // The declared MIME type (from the data URI header, or the "image/jpeg" default for a
            // headerless payload) isn't always trustworthy — a mislabeled upload sends the wrong
            // media_type to Anthropic alongside the real bytes, which Anthropic rejects outright.
            // Self-correct when we can confidently sniff a different, actually-supported format;
            // otherwise keep trusting the declared type unchanged.
            var sniffedMimeType = ToMimeType(DetectImageFormatFromBytes(imageBytes));
            if (sniffedMimeType != null && !string.Equals(sniffedMimeType, mimeType, StringComparison.OrdinalIgnoreCase))
                mimeType = sniffedMimeType;

            return (imageBytes, mimeType);
        }

        public static (byte[] bytes, string mimeType, string extractedText) ParseDocumentData(this string documentData)
        {
            if (string.IsNullOrWhiteSpace(documentData))
                throw new ArgumentNullException(nameof(documentData));

            string mimeType = "application/octet-stream";
            byte[] documentBytes;
            string extractedText = string.Empty;

            if (documentData.StartsWith("data:"))
            {
                var headerEnd = documentData.IndexOf(',');
                if (headerEnd <= 0)
                    throw new ArgumentException("Invalid data URI format", nameof(documentData));

                var header = documentData.Substring(0, headerEnd); // "data:application/pdf;base64"
                var mimeTypePart = header.Split(';')[0]; // "data:application/pdf"
                mimeType = mimeTypePart.Replace("data:", ""); // "application/pdf"

                documentBytes = DecodeBase64(documentData, headerEnd + 1);

                extractedText = ExtractTextFromDocument(documentBytes, mimeType);
            }
            else
            {
                documentBytes = Convert.FromBase64String(documentData);
                extractedText = ExtractTextFromDocument(documentBytes, mimeType);
            }

            return (documentBytes, mimeType, extractedText);
        }

        #region Private

        private static string ExtractTextFromDocument(byte[] documentBytes, string mimeType)
        {
            return mimeType.ToLowerInvariant() switch
            {
                "text/plain" => System.Text.Encoding.UTF8.GetString(documentBytes),
                "application/pdf" => ExtractPdfText(documentBytes),
                "application/msword" => ExtractDocText(documentBytes),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractDocxText(documentBytes),
                "application/vnd.ms-excel" => ExtractXlsText(documentBytes),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ExtractXlsxText(documentBytes),
                "text/csv" => System.Text.Encoding.UTF8.GetString(documentBytes),
                "application/json" => System.Text.Encoding.UTF8.GetString(documentBytes),
                "application/xml" => System.Text.Encoding.UTF8.GetString(documentBytes),
                "text/xml" => System.Text.Encoding.UTF8.GetString(documentBytes),
                _ => "Text extraction not supported for this file type"
            };
        }

        private static string ExtractDocText(byte[] docBytes)
        {
            try
            {
                return DocTextExtractor.Extract(docBytes);
            }
            catch (Exception ex)
            {
                return $"Error extracting DOC text: {ex.Message}";
            }
        }

        private static string ExtractPdfText(byte[] pdfBytes)
        {
            try
            {
                using var pdfDocument = PdfDocument.Open(pdfBytes);

                var textBuilder = new StringBuilder();

                // Extraer texto de todas las páginas
                foreach (var page in pdfDocument.GetPages())
                {
                    textBuilder.AppendLine($"--- Page {page.Number} ---");
                    textBuilder.AppendLine(ContentOrderTextExtractor.GetText(page));
                    textBuilder.AppendLine();
                }

                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                return $"Error extracting PDF text: {ex.Message}";
            }
        }

        private static string ExtractDocxText(byte[] docxBytes)
        {
            try
            {
                using var memoryStream = new MemoryStream(docxBytes);
                using var document = WordprocessingDocument.Open(memoryStream, false);

                var body = document.MainDocumentPart?.Document?.Body;
                if (body == null)
                    return "No content found in document";

                return body.InnerText;
            }
            catch (Exception ex)
            {
                return $"Error extracting DOCX text: {ex.Message}";
            }
        }

        private static string ExtractXlsxText(byte[] xlsxBytes)
        {
            try
            {
                using var memoryStream = new MemoryStream(xlsxBytes);
                using var spreadsheetDocument = SpreadsheetDocument.Open(memoryStream, false);

                var workbookPart = spreadsheetDocument.WorkbookPart;
                if (workbookPart == null)
                    return "No content found in spreadsheet";

                var stringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                var textBuilder = new StringBuilder();

                // Iterate through all worksheets
                var sheets = workbookPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>();

                foreach (var sheet in sheets)
                {
                    textBuilder.AppendLine($"--- Sheet: {sheet.Name} ---");

                    var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
                    var worksheet = worksheetPart.Worksheet;
                    var sheetData = worksheet.GetFirstChild<SheetData>();

                    if (sheetData != null)
                    {
                        foreach (var row in sheetData.Descendants<Row>())
                        {
                            foreach (var cell in row.Descendants<Cell>())
                            {
                                var cellValue = GetCellValue(cell, stringTable);
                                if (!string.IsNullOrWhiteSpace(cellValue))
                                {
                                    textBuilder.Append(cellValue);
                                    textBuilder.Append("\t");
                                }
                            }
                            textBuilder.AppendLine();
                        }
                    }
                    textBuilder.AppendLine();
                }

                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                return $"Error extracting XLSX text: {ex.Message}";
            }
        }

        // ExcelDataReader decodes legacy .xls code pages (e.g. 1252) and needs this provider registered.
        // It ships with .NET (no extra package); registering is idempotent, Lazy just makes it once.
        private static readonly Lazy<bool> CodePagesRegistered = new(() =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return true;
        });

        // Work budget for .xls: ExcelDataReader trusts the dimensions a file declares, so a tiny forged file can claim
        // 65,536 columns x 64,001 rows per sheet, or even 2 billion rows (its final Read() then loops internally over
        // every declared row without returning). 65,536 rows and 256 columns are the real BIFF8 limits; a sheet that
        // declares more is skipped before any Read().
        private const int MaxXlsColumns = 256;
        private const int MaxXlsRows = 65_536;
        private const long MaxXlsCellsVisited = 10_000_000;
        private const int MaxXlsCharacters = 10_000_000;
        private const string TruncatedMarker = "[truncated]";

        private static string ExtractXlsText(byte[] xlsBytes)
        {
            try
            {
                _ = CodePagesRegistered.Value;

                using var memoryStream = new MemoryStream(xlsBytes);
                using var reader = ExcelReaderFactory.CreateBinaryReader(memoryStream);

                var textBuilder = new StringBuilder();
                long cellsVisited = 0;
                var truncated = false;
                var budgetExhausted = false;

                // Same shape as ExtractXlsxText: one block per sheet, one line per row, non-empty cells tab-separated
                do
                {
                    if (reader.RowCount > MaxXlsRows || reader.FieldCount > MaxXlsColumns)
                    {
                        truncated = true; // forged dimensions: skip this sheet without reading it
                        continue;
                    }

                    textBuilder.AppendLine($"--- Sheet: {reader.Name} ---");

                    while (!budgetExhausted && reader.Read())
                    {
                        var columns = reader.FieldCount;
                        cellsVisited += Math.Max(columns, 1);
                        if (cellsVisited > MaxXlsCellsVisited || textBuilder.Length > MaxXlsCharacters)
                        {
                            truncated = true;
                            budgetExhausted = true;
                            break;
                        }

                        var rowHasContent = false;
                        for (var column = 0; column < columns; column++)
                        {
                            var cellValue = FormatXlsCellValue(reader.GetValue(column));
                            if (!string.IsNullOrWhiteSpace(cellValue))
                            {
                                // A single BIFF8 row can hold ~8M chars, so check the budget per cell, not just per row
                                if ((long)textBuilder.Length + cellValue.Length + 1 > MaxXlsCharacters)
                                {
                                    truncated = true;
                                    budgetExhausted = true;
                                    break;
                                }

                                textBuilder.Append(cellValue);
                                textBuilder.Append("\t");
                                rowHasContent = true;
                            }
                        }

                        if (rowHasContent)
                            textBuilder.AppendLine();
                    }
                    textBuilder.AppendLine();
                } while (!budgetExhausted && reader.NextResult());

                if (truncated)
                    textBuilder.AppendLine(TruncatedMarker);

                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                return $"Error extracting XLS text: {ex.Message}";
            }
        }

        private static string FormatXlsCellValue(object value) => value switch
        {
            null => string.Empty,
            bool b => b ? "TRUE" : "FALSE",
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss"),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        private static string GetCellValue(Cell cell, SharedStringTable stringTable)
        {
            var value = cell.InnerText;

            if (cell.DataType?.Value != null)
            {
                if (cell.DataType.Value == CellValues.SharedString)
                {
                    if (stringTable != null && int.TryParse(value, out var stringIndex))
                    {
                        var sharedString = stringTable.Elements<SharedStringItem>().ElementAtOrDefault(stringIndex);
                        return sharedString?.Text?.Text ?? sharedString?.InnerText ?? "";
                    }
                }
                else if (cell.DataType.Value == CellValues.Boolean)
                {
                    return value == "1" ? "TRUE" : "FALSE";
                }
                else if (cell.DataType.Value == CellValues.Date)
                {
                    if (double.TryParse(value, out var dateValue))
                    {
                        var date = DateTime.FromOADate(dateValue);
                        return date.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }

            return value;
        }

        #endregion
    }
}
