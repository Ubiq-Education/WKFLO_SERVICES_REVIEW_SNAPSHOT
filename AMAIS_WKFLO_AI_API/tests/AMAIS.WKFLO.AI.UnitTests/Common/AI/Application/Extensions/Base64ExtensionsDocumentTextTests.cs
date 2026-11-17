using AMAIS.WKFLO.Common.AI.Application.Extensions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Extensions
{
    /// <summary>
    /// Text extraction for chat document attachments (<see cref="Base64Extensions.ParseDocumentData"/>),
    /// run against real sample files in TestData/Documents.
    /// </summary>
    public class Base64ExtensionsDocumentTextTests
    {
        private const string PdfMime = "application/pdf";
        private const string DocMime = "application/msword";
        private const string XlsMime = "application/vnd.ms-excel";
        private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private static byte[] Sample(string fileName) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Documents", fileName));

        // Built the same way the chat flow sends attachments: a base64 data URL.
        private static string Extract(string mimeType, byte[] bytes) =>
            $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}".ParseDocumentData().extractedText;

        // ---------- PDF ----------

        [Fact]
        public void Pdf_SinglePage_ReturnsPageText()
        {
            var text = Extract(PdfMime, Sample("single-page.pdf"));

            Assert.Contains("--- Page 1 ---", text);
            Assert.Contains("WKFlo single page PDF sample", text);
            Assert.Contains("Second line of the single page document.", text);
            Assert.Matches(@"sample\r?\nSecond line", text); // lines keep their breaks
            Assert.DoesNotContain("Error extracting", text);
        }

        [Fact]
        public void Pdf_MultiPage_ReturnsEveryPageInOrder()
        {
            var text = Extract(PdfMime, Sample("multi-page.pdf"));

            Assert.Contains("Heading of page 1", text);
            Assert.Contains("Body text on page 2 of the multi-page sample.", text);
            Assert.Contains("Body text on page 3 of the multi-page sample.", text);
            Assert.True(text.IndexOf("--- Page 1 ---", StringComparison.Ordinal) < text.IndexOf("--- Page 2 ---", StringComparison.Ordinal));
            Assert.True(text.IndexOf("--- Page 2 ---", StringComparison.Ordinal) < text.IndexOf("--- Page 3 ---", StringComparison.Ordinal));
        }

        [Fact]
        public void Pdf_CorruptBytes_ReturnsErrorMessage()
        {
            var text = Extract(PdfMime, Encoding.ASCII.GetBytes("this is not a pdf"));

            Assert.StartsWith("Error extracting PDF text:", text);
        }

        // ---------- XLS ----------

        [Fact]
        public void Xls_MultiSheet_ReturnsEverySheetRowByRow()
        {
            var text = Extract(XlsMime, Sample("SampleSS.xls"));

            Assert.Contains("--- Sheet: First Sheet ---", text);
            Assert.Contains("Test spreadsheet\t", text);
            Assert.Contains("2nd row\t2nd row 2nd column\t", text);
            Assert.Contains("--- Sheet: Sheet Number 2 ---", text);
            Assert.Contains("cb=1\tcb=10\tcb=2\tcb=sum\t", text);
            Assert.Contains("1\t10\t2\t13\t", text);
            Assert.DoesNotContain("Error extracting", text);
        }

        [Fact]
        public void Xls_DateCells_AreFormattedLikeXlsx()
        {
            var text = Extract(XlsMime, Sample("1900DateWindowing.xls"));

            Assert.Contains("2000-01-01 00:00:00", text);
        }

        [Fact]
        public void Xls_UnicodeText_IsPreserved()
        {
            var text = Extract(XlsMime, Sample("chinese-provinces.xls"));

            Assert.Contains("Provinces in China", text);
            Assert.Contains("Anhui\t\u5B89\u5FBD\u7701\t", text);
            Assert.Contains("Beijing\t\u5317\u4EAC\u5E02\t", text);
        }

        [Fact]
        public void Xls_CorruptBytes_ReturnsErrorMessage()
        {
            var text = Extract(XlsMime, Encoding.ASCII.GetBytes("this is not a spreadsheet"));

            Assert.StartsWith("Error extracting XLS text:", text);
        }

        // ---------- DOC ----------

        [Fact]
        public void Doc_Plain_ReturnsTextWithoutEvaluationNotice()
        {
            var text = Extract(DocMime, Sample("simple.doc"));

            Assert.Equal("This is a simple file created with Word 97-SR2.\n", text);
        }

        [Fact]
        public void Doc_EightBitPieceWithWindows1252Characters_AreDecoded()
        {
            var text = Extract(DocMime, Sample("SampleDoc.doc"));

            Assert.Contains("I am a test document", text);
            Assert.Contains("It’s Arial Black in 16 point", text); // 0x92 in the 8-bit piece
        }

        [Fact]
        public void Doc_UnicodePieces_AreDecodedAndLimitedToMainText()
        {
            var text = Extract(DocMime, Sample("HeaderFooterUnicode.doc"));

            Assert.Contains("GBP - £", text);
            Assert.Contains("EUR - €", text);
            Assert.Contains("Molière", text);
            Assert.Contains("L'Avare ou l'École du mensonge", text);
            Assert.Contains("Les Précieuses ridicules. The end.", text);
        }

        [Fact]
        public void Doc_MultiplePiecesMixingEightBitAndUtf16_AreStitchedInOrder()
        {
            var text = Extract(DocMime, Sample("Bug47742.doc"));

            Assert.StartsWith("{0>Der Aaa Satz.<}0{>The Aaa phrase.<0} ", text);
            Assert.Contains("Ein—Geviertstrich hier.", text); // em dash lives in a UTF-16 piece
            Assert.Contains("A/slash here.", text);
            Assert.Contains("A soft\nreturn in the center.", text); // manual line break becomes a newline
        }

        [Fact]
        public void Doc_Fields_KeepResultAndDropFieldCodes()
        {
            var text = Extract(DocMime, Sample("test-fields.doc"));

            Assert.Contains("19/11/2010 14:49:00", text);
            Assert.Contains("Here is a link to an endnote", text);
            Assert.DoesNotContain('\u0013', text);
            Assert.DoesNotContain('\u0014', text);
            Assert.DoesNotContain('\u0015', text);
        }

        [Fact]
        public void Doc_CorruptBytes_ReturnsErrorMessage()
        {
            var text = Extract(DocMime, Encoding.ASCII.GetBytes("this is not a word document"));

            Assert.StartsWith("Error extracting DOC text:", text);
        }

        [Fact]
        public void Doc_TruncatedFile_ReturnsErrorMessage()
        {
            var bytes = Sample("simple.doc");

            var text = Extract(DocMime, bytes[..(bytes.Length / 2)]);

            Assert.StartsWith("Error extracting DOC text:", text);
        }

        [Fact]
        public void Doc_EncryptedFlag_ReturnsErrorMessage()
        {
            // fEncrypted is bit 0x0100 of the FIB flags word at offset 0x0A of the WordDocument stream.
            // Locate the stream by its FIB signature (wIdent 0xA5EC, nFib 0x00C1) and set the bit in place.
            var bytes = (byte[])Sample("simple.doc").Clone();
            var index = bytes.AsSpan().IndexOf(new byte[] { 0xEC, 0xA5, 0xC1, 0x00 });
            Assert.True(index >= 0, "FIB signature not found in sample");
            bytes[index + 0x0B] |= 0x01;

            var text = Extract(DocMime, bytes);

            Assert.StartsWith("Error extracting DOC text:", text);
            Assert.Contains("encrypted", text);
        }

        // ---------- DOC: malformed / hostile input must fail fast, never hang ----------

        private static string ExtractWithinTimeout(string mimeType, byte[] bytes)
        {
            var task = Task.Run(() => Extract(mimeType, bytes));
            Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "Extraction did not finish in time (possible infinite loop).");
            return task.Result;
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            var index = data.AsSpan().IndexOf(pattern);
            Assert.True(index >= 0, "Expected byte pattern not found in sample");
            return index;
        }

        [Theory]
        [InlineData(0x7FFFFF00u)]
        [InlineData(uint.MaxValue)]
        [InlineData(8192u)]
        public void Doc_ForgedWordDocumentStreamSize_ReturnsQuicklyWithoutHanging(uint forgedSize)
        {
            // Directory entry layout: 64-byte UTF-16 name, ..., stream size (8 bytes) at offset 120.
            var bytes = (byte[])Sample("simple.doc").Clone();
            var entry = IndexOf(bytes, Encoding.Unicode.GetBytes("WordDocument"));
            BitConverter.GetBytes(forgedSize).CopyTo(bytes, entry + 120);
            BitConverter.GetBytes(0u).CopyTo(bytes, entry + 124);

            var text = ExtractWithinTimeout(DocMime, bytes);

            // 8192 exceeds the real 4096-byte chain, so even that case is rejected.
            Assert.StartsWith("Error extracting DOC text:", text);
        }

        [Fact]
        public void Doc_FatChainCycle_ReturnsErrorMessageWithoutHanging()
        {
            // Make the directory's first sector point to itself in the FAT.
            var bytes = (byte[])Sample("simple.doc").Clone();
            var sectorSize = 1 << BitConverter.ToUInt16(bytes, 0x1E);
            var firstDirSector = BitConverter.ToUInt32(bytes, 0x30);
            var firstFatSector = BitConverter.ToUInt32(bytes, 0x4C);
            var fatEntryOffset = (firstFatSector + 1L) * sectorSize + firstDirSector * 4L;
            BitConverter.GetBytes(firstDirSector).CopyTo(bytes, fatEntryOffset);

            var text = ExtractWithinTimeout(DocMime, bytes);

            Assert.StartsWith("Error extracting DOC text:", text);
        }

        [Fact]
        public void Doc_TruncatedInsideHeader_ReturnsErrorMessage()
        {
            var text = ExtractWithinTimeout(DocMime, Sample("simple.doc")[..300]);

            Assert.StartsWith("Error extracting DOC text:", text);
        }

        [Fact]
        public void Doc_PieceTableClaimingHugeText_IsRejected()
        {
            var original = Sample("simple.doc");
            var bytes = (byte[])original.Clone();

            // ccpText (4th FibRgLw97 value) and the single piece's end character position are forged to ~2 billion.
            var fib = IndexOf(bytes, [0xEC, 0xA5, 0xC1, 0x00]);
            var csw = BitConverter.ToUInt16(bytes, fib + 32);
            var ccpTextOffset = fib + 32 + 2 + csw * 2 + 2 + 3 * 4;
            BitConverter.GetBytes(0x7FFFFFF0).CopyTo(bytes, ccpTextOffset);

            // Pcdt: clxt 0x02, lcbPlcPcd = 16 (one piece), cp[0] = 0, cp[1] = piece length.
            var pcdt = IndexOf(bytes, [0x02, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
            BitConverter.GetBytes(0x7FFFFFF0).CopyTo(bytes, pcdt + 9);

            var text = ExtractWithinTimeout(DocMime, bytes);

            // The forged piece claims more bytes than the stream holds, so the bounds check rejects it.
            Assert.StartsWith("Error extracting DOC text:", text);
            Assert.Contains("outside the document stream", text);
        }

        // ---------- XLS: forged dimensions must hit the work budget ----------

        [Fact]
        public void Xls_ForgedSheetDimensions_FinishesQuicklyWithTruncatedTextOrError()
        {
            var text = ExtractWithinTimeout(XlsMime, Sample("forged-dimensions.xls"));

            Assert.EndsWith("[truncated]" + Environment.NewLine, text);
        }

        [Theory]
        [InlineData("forged-rows-single-sheet.xls")]
        [InlineData("forged-rows-three-sheets.xls")]
        public void Xls_ForgedRowCount_IsSkippedQuicklyAndMarkedTruncated(string fileName)
        {
            var text = ExtractWithinTimeout(XlsMime, Sample(fileName));

            Assert.EndsWith("[truncated]" + Environment.NewLine, text);
        }

        [Fact]
        public void Xls_FatSectorCountBeyondWhatTheFileProvides_ReportsIncompleteFat()
        {
            var bytes = (byte[])Sample("simple.doc").Clone();
            BitConverter.GetBytes(5u).CopyTo(bytes, 0x2C);

            var exception = Assert.Throws<InvalidDataException>(() => CompoundFileReader.ReadStream(bytes, "WordDocument"));
            Assert.Contains("incomplete", exception.Message);
        }

        // ---------- CFB reader ----------

        [Fact]
        public void Doc_HighDwordOfStreamSizeInVersion3File_IsIgnored()
        {
            var bytes = (byte[])Sample("simple.doc").Clone();
            var entry = IndexOf(bytes, Encoding.Unicode.GetBytes("WordDocument"));
            BitConverter.GetBytes(0xDEADBEEFu).CopyTo(bytes, entry + 124);

            var text = ExtractWithinTimeout(DocMime, bytes);

            Assert.Equal("This is a simple file created with Word 97-SR2.\n", text);
        }

        [Fact]
        public void CompoundFile_MiniStream_IsReadCorrectly()
        {
            // \u0001CompObj is small, so it lives in the mini stream.
            var stream = CompoundFileReader.ReadStream(Sample("simple.doc"), "\u0001CompObj");

            Assert.True(stream.Length >= 4);
            Assert.Equal(new byte[] { 0x01, 0x00, 0xFE, 0xFF }, stream[..4]);
        }

        [Fact]
        public void CompoundFile_DirectoryTreeCycle_Throws()
        {
            var bytes = (byte[])Sample("simple.doc").Clone();
            var sectorSize = 1 << BitConverter.ToUInt16(bytes, 0x1E);
            var dirOffset = (BitConverter.ToUInt32(bytes, 0x30) + 1L) * sectorSize;
            // Every entry in the first directory sector gets itself as its left sibling.
            for (var i = 0; i < sectorSize / 128; i++)
                BitConverter.GetBytes((uint)i).CopyTo(bytes, dirOffset + i * 128L + 68);

            var exception = Assert.Throws<InvalidDataException>(() => CompoundFileReader.ReadStream(bytes, "NoSuchStream"));
            Assert.Contains("cycle", exception.Message);
        }

        [Theory]
        [InlineData(0x2C, 0x7FFFFFFFu)] // FAT sector count larger than the file
        [InlineData(0x2C, 5u)]          // more FAT sectors than the header and DIFAT provide
        [InlineData(0x48, 0x7FFFFFFFu)] // DIFAT sector count larger than the file
        public void Doc_ForgedFatOrDifatCounts_ReturnErrorMessage(int headerOffset, uint forgedValue)
        {
            var bytes = (byte[])Sample("simple.doc").Clone();
            BitConverter.GetBytes(forgedValue).CopyTo(bytes, headerOffset);

            var text = ExtractWithinTimeout(DocMime, bytes);

            Assert.StartsWith("Error extracting DOC text:", text);
        }

        // ---------- Regression: formats that did not change ----------

        [Fact]
        public void Docx_StillExtractsBodyText()
        {
            using var stream = new MemoryStream();
            using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Hello docx body")))));
            }

            var text = Extract(DocxMime, stream.ToArray());

            Assert.Equal("Hello docx body", text);
        }

        [Fact]
        public void Xlsx_StillExtractsSheetsAndCells()
        {
            using var stream = new MemoryStream();
            using (var spreadsheet = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = spreadsheet.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData(
                    new Row(
                        new Cell { CellValue = new CellValue("Alpha"), DataType = CellValues.String },
                        new Cell { CellValue = new CellValue("42"), DataType = CellValues.Number })));
                workbookPart.Workbook.AppendChild(new Sheets(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Data"
                }));
            }

            var text = Extract(XlsxMime, stream.ToArray());

            Assert.Contains("--- Sheet: Data ---", text);
            Assert.Contains("Alpha\t42\t", text);
        }

        [Theory]
        [InlineData("text/plain", "plain text ñ")]
        [InlineData("text/csv", "a,b\n1,2")]
        [InlineData("application/json", "{\"a\":1}")]
        [InlineData("application/xml", "<a>1</a>")]
        [InlineData("text/xml", "<a>1</a>")]
        public void TextBasedFormats_ReturnContentAsIs(string mimeType, string content)
        {
            var text = Extract(mimeType, Encoding.UTF8.GetBytes(content));

            Assert.Equal(content, text);
        }

        [Fact]
        public void UnsupportedMimeType_ReturnsNotSupportedMessage()
        {
            var text = Extract("application/zip", [1, 2, 3]);

            Assert.Equal("Text extraction not supported for this file type", text);
        }
    }
}
