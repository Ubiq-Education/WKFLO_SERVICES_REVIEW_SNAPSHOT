using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    public class DocumentChunkerTests
    {
        private static string Paragraphs(int count, int size)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                sb.Append($"Paragraph {i} ").Append('x', size - 14).Append("\n\n");
            }

            return sb.ToString();
        }

        [Fact]
        public void EmptyText_GivesNoChunks()
        {
            Assert.Empty(DocumentChunker.Chunk(string.Empty));
            Assert.Empty(DocumentChunker.Chunk(null));
            Assert.Empty(DocumentChunker.Chunk(new string(' ', 60_000) + "\n\n\t"));
        }

        [Theory]
        [InlineData(10, 20)]
        [InlineData(1, 1)]
        public void AHardCut_NeverSplitsASurrogatePair(int targetSize, int maxSize)
        {
            // No break characters at all, so every chunk ends at a hard cut; emoji are two UTF-16 chars each.
            var text = string.Concat(Enumerable.Repeat("a😀", 200));

            var spans = DocumentChunker.Chunk(text, targetSize, maxSize).ToList();

            Assert.All(spans, s =>
            {
                Assert.False(char.IsHighSurrogate(text[s.Offset + s.Length - 1]), $"chunk at {s.Offset} ends on a high surrogate");
                Assert.False(char.IsLowSurrogate(text[s.Offset]), $"chunk at {s.Offset} starts on a low surrogate");
            });
            Assert.Equal(text.Length, spans.Sum(s => s.Length));
        }

        [Fact]
        public void ShortText_IsOneChunk()
        {
            var chunks = DocumentChunker.Chunk("Hello world").ToList();

            var chunk = Assert.Single(chunks);
            Assert.Equal(new DocumentChunkSpan(0, 11, null), chunk);
        }

        [Fact]
        public void LongText_ChunksStayWithinTheMaximum_AreOrdered_AndLoseNoText()
        {
            var text = Paragraphs(400, 700);

            var chunks = DocumentChunker.Chunk(text, 1_500, 3_000).ToList();

            Assert.True(chunks.Count > 100);
            Assert.All(chunks, c => Assert.InRange(c.Length, 1, 3_000));
            for (var i = 1; i < chunks.Count; i++)
                Assert.True(chunks[i].Offset >= chunks[i - 1].Offset + chunks[i - 1].Length);

            // Only whitespace is ever left out.
            var covered = chunks.Sum(c => c.Length);
            var nonWhitespace = text.Count(c => !char.IsWhiteSpace(c));
            Assert.Equal(nonWhitespace, chunks.Sum(c => text.Substring(c.Offset, c.Length).Count(ch => !char.IsWhiteSpace(ch))));
            Assert.True(covered <= text.Length);
        }

        [Fact]
        public void ChunksPreferParagraphBreaks()
        {
            var text = Paragraphs(20, 700);

            var chunks = DocumentChunker.Chunk(text, 1_500, 3_000).ToList();

            // Two 700-character paragraphs fit the target, a third does not: every chunk ends on a paragraph end.
            Assert.All(chunks.Take(chunks.Count - 1), c => Assert.EndsWith("x", text.Substring(c.Offset, c.Length)));
            Assert.All(chunks.Take(chunks.Count - 1), c => Assert.Equal(2, text.Substring(c.Offset, c.Length).Split("Paragraph ", StringSplitOptions.RemoveEmptyEntries).Length));
        }

        [Fact]
        public void LongUnbrokenRun_IsCutAtTheTarget()
        {
            var text = new string('a', 7_000);

            var chunks = DocumentChunker.Chunk(text, 1_500, 3_000).ToList();

            Assert.All(chunks, c => Assert.True(c.Length <= 1_500));
            Assert.Equal(7_000, chunks.Sum(c => c.Length));
        }

        [Fact]
        public void PageMarkers_AreHardBreaks_AndGivePageNumbers()
        {
            var text = "--- Page 1 ---\nFirst page text.\n\n--- Page 2 ---\nSecond page text.\n\n--- Page 3 ---\n";

            var chunks = DocumentChunker.Chunk(text).ToList();

            Assert.Equal(2, chunks.Count);
            Assert.Equal("First page text.", text.Substring(chunks[0].Offset, chunks[0].Length));
            Assert.Equal(1, chunks[0].Page);
            Assert.Equal("Second page text.", text.Substring(chunks[1].Offset, chunks[1].Length));
            Assert.Equal(2, chunks[1].Page);
        }

        [Fact]
        public void SheetMarkers_AreHardBreaks_WithNoPage()
        {
            var text = "--- Sheet: Budget ---\nA 1\nB 2\n--- Sheet: Plan ---\nC 3\n";

            var chunks = DocumentChunker.Chunk(text).ToList();

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, c => Assert.Null(c.Page));
            Assert.DoesNotContain("---", text.Substring(chunks[0].Offset, chunks[0].Length));
            Assert.Equal("C 3", text.Substring(chunks[1].Offset, chunks[1].Length));
        }

        [Fact]
        public void ADashLineThatIsNotAMarker_StaysInTheText()
        {
            var text = "Intro\n--- not a marker ---\nMore";

            var chunk = Assert.Single(DocumentChunker.Chunk(text));

            Assert.Equal(text, text.Substring(chunk.Offset, chunk.Length));
        }

        [Fact]
        public void SameInput_GivesSameChunks()
        {
            var text = Paragraphs(200, 650);

            Assert.Equal(DocumentChunker.Chunk(text).ToList(), DocumentChunker.Chunk(text).ToList());
        }
    }

    public class CoverageOrderTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(1_000)]
        [InlineData(2_001)]
        public void EveryIndexAppearsExactlyOnce(int count)
        {
            var order = CoverageOrder.Generate(count).ToList();

            Assert.Equal(Enumerable.Range(0, count), order.OrderBy(i => i));
        }

        [Fact]
        public void StartsWithTheMiddlePointsSoAnEarlyStopStillCoversTheDocument()
        {
            Assert.Equal(new List<int> { 0, 4, 2, 6, 1, 5, 3, 7 }, CoverageOrder.Generate(8).ToList());

            var firstQuarter = CoverageOrder.Generate(1_000).Take(250).OrderBy(i => i).ToList();
            Assert.True(firstQuarter[0] < 10);
            Assert.True(firstQuarter[^1] > 980);
            Assert.All(firstQuarter.Zip(firstQuarter.Skip(1)), p => Assert.True(p.Second - p.First <= 8));
        }
    }
}
