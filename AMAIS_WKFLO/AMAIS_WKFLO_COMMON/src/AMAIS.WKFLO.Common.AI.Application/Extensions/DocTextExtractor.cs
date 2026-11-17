using System.Buffers.Binary;
using System.Text;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    /// <summary>
    /// Extracts the main document text from legacy Word 97-2003 (.doc) files.
    /// A small internal CFB reader opens the OLE2 container; the text itself is rebuilt here from the
    /// piece table described in [MS-DOC] (FIB, CLX, PlcPcd), without a full Word object model.
    /// </summary>
    internal static class DocTextExtractor
    {
        private const string WordDocumentStream = "WordDocument";

        // FibBase offsets and flags ([MS-DOC] 2.5.2)
        private const int FibFlagsOffset = 0x0A;
        private const ushort FlagEncrypted = 0x0100;
        private const ushort FlagWhichTable = 0x0200; // set: 1Table, clear: 0Table
        private const int FibBaseSize = 32;
        private const int MinSupportedNFib = 0x00C1; // Word 97; older versions have no CLX piece table

        // Piece descriptor (PCD) layout and the "text is 8-bit" marker in its fc field
        private const int PcdSize = 8;
        private const uint FcCompressedFlag = 0x40000000;

        // Main-text control characters
        private const char FieldBegin = '\u0013';
        private const char FieldSeparator = '\u0014';
        private const char FieldEnd = '\u0015';

        /// <summary>Returns the main document text. Throws on encrypted or malformed files.</summary>
        public static string Extract(byte[] docBytes)
        {
            var compoundFile = CompoundFileReader.Open(docBytes);
            var wordDocument = compoundFile.GetStream(WordDocumentStream);
            var fib = ReadFib(wordDocument);

            var tableStream = compoundFile.GetStream(fib.UsesTable1 ? "1Table" : "0Table");
            var rawText = ReadPieces(wordDocument, tableStream, fib);
            return CleanText(rawText);
        }

        private readonly record struct Fib(bool UsesTable1, int CcpText, int FcClx, int LcbClx);

        private static Fib ReadFib(byte[] wordDocument)
        {
            if (wordDocument.Length < FibBaseSize + 2)
                throw new InvalidDataException("The WordDocument stream is too short.");

            var nFib = BinaryPrimitives.ReadUInt16LittleEndian(wordDocument.AsSpan(2));
            if (nFib < MinSupportedNFib)
                throw new InvalidDataException("Word 95 or earlier documents are not supported.");

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(wordDocument.AsSpan(FibFlagsOffset));
            if ((flags & FlagEncrypted) != 0)
                throw new InvalidDataException("The document is encrypted or password protected.");

            // The FIB is a chain of variable-length arrays, each prefixed by its size:
            // FibBase, csw + FibRgW (16-bit values), cslw + FibRgLw (32-bit values), cbRgFcLcb + FibRgFcLcb.
            var csw = ReadUInt16(wordDocument, FibBaseSize);
            var rgLwStart = FibBaseSize + 2 + csw * 2 + 2;
            var cslw = ReadUInt16(wordDocument, rgLwStart - 2);
            var rgFcLcbStart = rgLwStart + cslw * 4 + 2;
            var cbRgFcLcb = ReadUInt16(wordDocument, rgFcLcbStart - 2);

            // ccpText is the 4th value of FibRgLw97 (length of the main document text in characters).
            var ccpText = ReadInt32(wordDocument, rgLwStart + 3 * 4);

            // fcClx / lcbClx are the 34th fc/lcb pair of FibRgFcLcb97.
            const int clxPairIndex = 33;
            if (cbRgFcLcb <= clxPairIndex)
                throw new InvalidDataException("The file header does not contain a piece table location.");

            var fcClx = ReadInt32(wordDocument, rgFcLcbStart + clxPairIndex * 8);
            var lcbClx = ReadInt32(wordDocument, rgFcLcbStart + clxPairIndex * 8 + 4);

            return new Fib((flags & FlagWhichTable) != 0, ccpText, fcClx, lcbClx);
        }

        private static string ReadPieces(byte[] wordDocument, byte[] tableStream, Fib fib)
        {
            if (fib.CcpText <= 0)
                return string.Empty;

            if (fib.FcClx < 0 || fib.LcbClx <= 0 || (long)fib.FcClx + fib.LcbClx > tableStream.Length)
                throw new InvalidDataException("The piece table lies outside the table stream.");

            // CLX = zero or more Prc entries (clxt 0x01: formatting, skipped) followed by one Pcdt (clxt 0x02).
            long pos = fib.FcClx;
            long end = (long)fib.FcClx + fib.LcbClx;
            while (pos < end && tableStream[pos] == 0x01)
            {
                var cbGrpprl = ReadUInt16(tableStream, pos + 1);
                pos += 1 + 2 + cbGrpprl;
            }

            if (pos >= end || tableStream[pos] != 0x02)
                throw new InvalidDataException("The piece table (Pcdt) was not found.");

            var lcbPlcPcd = ReadInt32(tableStream, pos + 1);
            long plcStart = pos + 5;
            if (lcbPlcPcd < 4 + 4 + PcdSize || plcStart + lcbPlcPcd > end)
                throw new InvalidDataException("The piece table is truncated.");

            // PlcPcd = (n + 1) character positions (4 bytes each) followed by n PCDs (8 bytes each),
            // so lcb = 4 * (n + 1) + 8 * n.
            var pieceCount = (lcbPlcPcd - 4) / (4 + PcdSize);
            long pcdStart = plcStart + (pieceCount + 1L) * 4;

            // Overlapping or duplicated piece descriptors could otherwise expand a tiny file enormously:
            // the extracted text can never legitimately exceed the size of the WordDocument stream.
            long maxChars = Math.Min(fib.CcpText, wordDocument.Length);

            var text = new StringBuilder((int)Math.Min(maxChars, 1 << 20));
            for (var i = 0; i < pieceCount && text.Length < maxChars; i++)
            {
                var cpStart = ReadInt32(tableStream, plcStart + i * 4L);
                var cpEnd = ReadInt32(tableStream, plcStart + (i + 1) * 4L);
                if (cpStart < 0 || cpEnd < cpStart)
                    throw new InvalidDataException("The piece table is corrupt.");

                // Only the main document is wanted; footnotes, headers etc. follow it in the same CP space.
                long charCount = Math.Min(cpEnd, fib.CcpText) - (long)cpStart;
                charCount = Math.Min(charCount, maxChars - text.Length);
                if (charCount <= 0)
                    continue;

                // PCD: 2 bytes flags, 4 bytes fc, 2 bytes prm.
                var fc = BinaryPrimitives.ReadUInt32LittleEndian(tableStream.AsSpan((int)(pcdStart + i * (long)PcdSize + 2), 4));
                var compressed = (fc & FcCompressedFlag) != 0;
                var fileOffset = (long)(fc & ~FcCompressedFlag);
                if (compressed)
                    fileOffset /= 2; // 8-bit text: fc stores the real byte offset multiplied by two

                var byteCount = compressed ? charCount : charCount * 2;
                if (fileOffset + byteCount > wordDocument.Length)
                    throw new InvalidDataException("A text piece lies outside the document stream.");

                var piece = wordDocument.AsSpan((int)fileOffset, (int)byteCount);
                if (compressed)
                {
                    foreach (var b in piece)
                        text.Append(DecodeCompressedByte(b));
                }
                else
                {
                    text.Append(Encoding.Unicode.GetString(piece));
                }
            }

            return text.ToString();
        }

        // 8-bit pieces are Windows-1252, with the 0x80-0x9F range remapped per [MS-DOC] 2.9.73.
        // Kept as a table so no CodePages encoding provider needs to be registered.
        private static readonly char[] Cp1252High =
        [
            '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡',
            'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
            '\u0090', '‘', '’', '“', '”', '•', '–', '—',
            '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ'
        ];

        private static char DecodeCompressedByte(byte b) =>
            b is >= 0x80 and <= 0x9F ? Cp1252High[b - 0x80] : (char)b;

        /// <summary>
        /// Turns Word control characters into plain text: paragraph/line/page breaks become newlines,
        /// table cell marks become tabs, field codes are dropped (their visible result is kept) and
        /// other control characters are removed.
        /// </summary>
        private static string CleanText(string raw)
        {
            var result = new StringBuilder(raw.Length);

            // One entry per open field: true once its separator was seen (we are in the visible result).
            var openFields = new Stack<bool>();
            var hiddenDepth = 0; // number of open fields still inside their code part

            foreach (var c in raw)
            {
                switch (c)
                {
                    case FieldBegin:
                        openFields.Push(false);
                        hiddenDepth++;
                        continue;
                    case FieldSeparator:
                        if (openFields.Count > 0 && !openFields.Peek())
                        {
                            openFields.Pop();
                            openFields.Push(true);
                            hiddenDepth--;
                        }
                        continue;
                    case FieldEnd:
                        if (openFields.Count > 0 && !openFields.Pop())
                            hiddenDepth--;
                        continue;
                }

                if (hiddenDepth > 0)
                    continue;

                switch (c)
                {
                    case '\r':      // paragraph mark
                    case '\u000B':  // manual line break
                    case '\u000C':  // page / section break
                        result.Append('\n');
                        break;
                    case '\u0007':  // table cell / row mark
                        result.Append('\t');
                        break;
                    case '\u001E':  // non-breaking hyphen
                        result.Append('-');
                        break;
                    case '\t':
                        result.Append(c);
                        break;
                    default:
                        if (!char.IsControl(c))
                            result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }

        private static int ReadUInt16(byte[] data, long offset)
        {
            if (offset < 0 || offset + 2 > data.Length)
                throw new InvalidDataException("Unexpected end of data.");
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)offset));
        }

        private static int ReadInt32(byte[] data, long offset)
        {
            if (offset < 0 || offset + 4 > data.Length)
                throw new InvalidDataException("Unexpected end of data.");
            return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)offset));
        }
    }
}
