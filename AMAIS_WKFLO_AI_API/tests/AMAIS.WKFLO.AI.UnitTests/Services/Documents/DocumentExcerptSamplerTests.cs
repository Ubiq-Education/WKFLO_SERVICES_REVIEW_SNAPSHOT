using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    public class DocumentExcerptSamplerTests
    {
        private static string Pages(int pages)
        {
            var sb = new StringBuilder();
            for (var p = 1; p <= pages; p++)
            {
                sb.Append($"--- Page {p} ---\n");
                for (var i = 0; i < 4; i++)
                    sb.Append($"Page {p} paragraph {i} ").Append('y', 600).Append("\n\n");
            }

            return sb.ToString();
        }

        [Fact]
        public void TextWithinTheBudget_IsReturnedUnchanged()
        {
            Assert.Equal("short text", DocumentExcerptSampler.Build("short text", 200_000));
            Assert.Equal(string.Empty, DocumentExcerptSampler.Build(null));
        }

        [Fact]
        public void LongText_StaysWithinTheBudget_StartsWithTheHead_AndEndsWithTheLastChunk()
        {
            var text = Pages(1_500);

            var excerpt = DocumentExcerptSampler.Build(text, 200_000);

            Assert.InRange(excerpt.Length, 150_000, 200_000);
            Assert.StartsWith("--- Page 1 ---", excerpt);
            Assert.Contains("Page 1500 paragraph 3", excerpt);
        }

        [Fact]
        public void LongText_IsSampledFromDifferentPlacesWithPositionMarkers()
        {
            var text = Pages(1_500);

            var excerpt = DocumentExcerptSampler.Build(text, 200_000);

            var percents = Regex.Matches(excerpt, @"~(\d+)% into the document, page").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            Assert.Contains(percents, p => p is >= 45 and <= 55);
            Assert.Contains(percents, p => p >= 95);
            Assert.True(percents.Distinct().Count() > 50);
        }

        [Fact]
        public void SameText_GivesTheSameExcerpt()
        {
            var text = Pages(1_200);

            Assert.Equal(DocumentExcerptSampler.Build(text, 100_000), DocumentExcerptSampler.Build(text, 100_000));
        }

        [Fact]
        public void TextWithNoBreaksAtAll_IsStillBounded()
        {
            var text = new string('z', 500_000);

            var excerpt = DocumentExcerptSampler.Build(text, 200_000);

            Assert.True(excerpt.Length <= 200_000);
            Assert.True(excerpt.Length > 100_000);
        }
    }
}
