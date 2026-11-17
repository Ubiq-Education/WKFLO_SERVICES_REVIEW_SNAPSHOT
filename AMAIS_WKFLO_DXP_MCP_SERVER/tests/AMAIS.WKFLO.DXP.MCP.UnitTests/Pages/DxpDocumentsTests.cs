using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests.Pages
{
    public class DxpDocumentsTests
    {
        internal static AggregationDocumentResponseResult Doc(string url, long views, string type = "Page", int unique = 0) =>
            new() { Object = url, TotalCount = views, UniqueCount = unique, Type = type };

        [Fact]
        public void TopPages_ReturnsOnlyPagesSortedByViewsAndCapped()
        {
            var documents = new[]
            {
                Doc("https://www.example.org/a", 10),
                Doc("https://www.example.org/file.pdf", 500, type: "Document"),
                Doc("https://www.example.org/b", 30),
                Doc("https://www.example.org/video", 400, type: "Video"),
                Doc("https://www.example.org/c", 20),
            };

            var result = DxpDocuments.TopPages(documents, 2);

            Assert.Equal(["https://www.example.org/b", "https://www.example.org/c"], result.Select(d => d.Object));
        }

        [Fact]
        public void TopPages_BreaksTiesByUniqueVisitors()
        {
            var documents = new[]
            {
                Doc("https://www.example.org/a", 10, unique: 1),
                Doc("https://www.example.org/b", 10, unique: 5),
            };

            Assert.Equal("https://www.example.org/b", DxpDocuments.TopPages(documents, 1).Single().Object);
        }

        [Fact]
        public void TopPages_WhenNull_ReturnsEmpty()
        {
            Assert.Empty(DxpDocuments.TopPages(null, 10));
        }
    }
}
