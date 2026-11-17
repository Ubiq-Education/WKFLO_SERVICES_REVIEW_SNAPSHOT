namespace AMAIS.WKFLO.Common.AI.Application.Services.Documents
{
    /// <summary>Where one chunk lies in the document text. <see cref="Page"/> is null for text before the first page marker and for sheets.</summary>
    public readonly record struct DocumentChunkSpan(int Offset, int Length, int? Page);

    /// <summary>
    /// Splits extracted document text into chunks of about <c>targetSize</c> characters (never more than <c>maxSize</c>),
    /// preferring paragraph, line and sentence breaks. One pass over the string by index: chunks are yielded as
    /// offsets, never copied into a list. A page or sheet marker line ("--- Page N ---", "--- Sheet: X ---") is a hard
    /// break: a chunk never crosses it, and the marker line itself is not part of a chunk.
    /// </summary>
    public static class DocumentChunker
    {
        private const string PageMarker = "--- Page ";
        private const string SheetMarker = "--- Sheet: ";

        public static IEnumerable<DocumentChunkSpan> Chunk(string text, int targetSize = 1_500, int maxSize = 3_000)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(targetSize, 1);
            maxSize = Math.Max(maxSize, targetSize);

            if (string.IsNullOrEmpty(text))
                yield break;

            var position = 0;
            int? page = null;
            var nextMarker = FindMarker(text, 0);

            while (position < text.Length)
            {
                if (position == nextMarker)
                {
                    // The marker line is consumed here and never reaches a chunk.
                    var lineEnd = text.IndexOf('\n', position);
                    var line = lineEnd < 0 ? text.AsSpan(position) : text.AsSpan(position, lineEnd - position);
                    page = ParsePage(line);
                    position = lineEnd < 0 ? text.Length : lineEnd + 1;
                    nextMarker = FindMarker(text, position);
                    continue;
                }

                if (char.IsWhiteSpace(text[position]))
                {
                    position++;
                    continue;
                }

                var boundary = Math.Min(text.Length, nextMarker);
                var limit = Math.Min(boundary, position + targetSize);
                var hardLimit = Math.Min(boundary, position + maxSize);

                var end = limit;
                if (limit < boundary)
                    end = FindBreak(text, position, limit, hardLimit);

                // Never split a UTF-16 surrogate pair across chunks: cut before the pair, or, when the pair starts the chunk,
                // after it (one character over the limit) so the loop always moves forward.
                if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
                    end = end - 1 > position ? end - 1 : end + 1;

                var length = TrimmedLength(text, position, end);
                if (length > 0)
                    yield return new DocumentChunkSpan(position, length, page);

                position = end;
            }
        }

        private static int FindBreak(string text, int start, int limit, int hardLimit)
        {
            var minimum = start + (limit - start) / 2;

            // Back from the target: paragraph, then line, then sentence, then any whitespace.
            var cut = LastBreak(text, "\n\n", minimum, limit);
            if (cut < 0) cut = LastBreak(text, "\n", minimum, limit);
            if (cut < 0) cut = LastBreak(text, ". ", minimum, limit);
            if (cut < 0) cut = LastWhitespace(text, minimum, limit);
            if (cut > start)
                return cut;

            // A very long unbroken run: look ahead, up to the maximum size, for the next line break.
            var ahead = text.IndexOf('\n', limit, hardLimit - limit);
            return ahead >= 0 ? ahead : limit;
        }

        private static int LastBreak(string text, string token, int minimum, int limit)
        {
            var window = limit - minimum;
            if (window < token.Length)
                return -1;

            var index = text.LastIndexOf(token, limit - 1, window, StringComparison.Ordinal);
            return index < 0 ? -1 : index + token.Length;
        }

        private static int LastWhitespace(string text, int minimum, int limit)
        {
            for (var i = limit - 1; i >= minimum; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i + 1;
            }

            return -1;
        }

        private static int TrimmedLength(string text, int start, int end)
        {
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;

            return end - start;
        }

        /// <summary>The offset of the next page/sheet marker line at or after <paramref name="from"/>, or int.MaxValue.</summary>
        private static int FindMarker(string text, int from)
        {
            while (from < text.Length)
            {
                var index = text.IndexOf("--- ", from, StringComparison.Ordinal);
                if (index < 0)
                    return int.MaxValue;

                if ((index == 0 || text[index - 1] == '\n')
                    && (text.AsSpan(index).StartsWith(PageMarker, StringComparison.Ordinal) || text.AsSpan(index).StartsWith(SheetMarker, StringComparison.Ordinal)))
                    return index;

                from = index + 1;
            }

            return int.MaxValue;
        }

        private static int? ParsePage(ReadOnlySpan<char> markerLine)
        {
            if (!markerLine.StartsWith(PageMarker, StringComparison.Ordinal))
                return null;

            var digits = markerLine[PageMarker.Length..];
            var count = 0;
            while (count < digits.Length && char.IsAsciiDigit(digits[count]))
                count++;

            return count > 0 && int.TryParse(digits[..count], out var page) ? page : null;
        }
    }
}
