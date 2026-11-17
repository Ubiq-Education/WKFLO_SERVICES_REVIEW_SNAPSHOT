using System.Text;

namespace AMAIS.WKFLO.Common.AI.Application.Services.Documents
{
    /// <summary>
    /// Builds a bounded excerpt of a long document taken from different places of it: the head (up to 10% of the budget),
    /// then evenly spaced chunks over the rest, always including the last one. Each piece after the head is introduced by a
    /// position marker. Deterministic: the same text always gives the same excerpt.
    /// </summary>
    public static class DocumentExcerptSampler
    {
        private const int AverageCostPerPiece = 1_700;

        public static string Build(string text, int maxChars = 200_000, int chunkTarget = 1_500, int chunkMax = 3_000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text ?? string.Empty;

            var headBudget = maxChars / 10;
            var builder = new StringBuilder(maxChars);

            var headEnd = 0;
            int[] picks = null;
            var nextPick = 0;
            DocumentChunkSpan? last = null;
            var lastTaken = false;

            // One pass over the chunks: the head first, then the first chunk reaching each evenly spaced pick.
            foreach (var chunk in DocumentChunker.Chunk(text, chunkTarget, chunkMax))
            {
                if (picks == null)
                {
                    if (chunk.Offset + chunk.Length <= headBudget)
                    {
                        headEnd = chunk.Offset + chunk.Length;
                        last = chunk;
                        continue;
                    }

                    builder.Append(text, 0, headEnd);
                    picks = BuildPicks(text.Length, headEnd, maxChars - headEnd - AverageCostPerPiece);
                }

                last = chunk;
                lastTaken = false;

                var chunkEnd = chunk.Offset + chunk.Length;
                if (nextPick < picks.Length && chunkEnd > picks[nextPick])
                {
                    // One chunk may satisfy several picks when the chunks are larger than the spacing.
                    while (nextPick < picks.Length && chunkEnd > picks[nextPick])
                        nextPick++;

                    // Keeps room for the last chunk, which is added below.
                    lastTaken = TryAppend(builder, text, chunk, maxChars - AverageCostPerPiece);
                }
            }

            if (picks == null)
                return text[..Math.Min(text.Length, maxChars)];

            if (last is { } final && !lastTaken)
                TryAppend(builder, text, final, maxChars);

            return builder.ToString();
        }

        private static int[] BuildPicks(int textLength, int headEnd, int budget)
        {
            var count = Math.Max(1, budget / AverageCostPerPiece);
            var span = textLength - headEnd;
            var picks = new int[count];
            for (var i = 0; i < count; i++)
                picks[i] = headEnd + (int)((long)span * i / count);

            return picks;
        }

        private static bool TryAppend(StringBuilder builder, string text, DocumentChunkSpan chunk, int budget)
        {
            var marker = FormatMarker(text.Length, chunk);
            if (builder.Length + marker.Length + chunk.Length > budget)
                return false;

            builder.Append(marker);
            builder.Append(text, chunk.Offset, chunk.Length);
            return true;
        }

        private static string FormatMarker(int textLength, DocumentChunkSpan chunk)
        {
            var percent = (int)((long)chunk.Offset * 100 / textLength);
            var page = chunk.Page is { } p ? $", page {p}" : string.Empty;
            return $"\n\n[... ~{percent}% into the document{page} ...]\n";
        }
    }
}
