using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.Page;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.MCP.UnitTests.Tools.Amais.Page
{
    public class AmaisPageToolsGetTemplatesTests
    {
        [Fact]
        public void SortByPopularity_WhenTemplatesNull_ReturnsNull()
        {
            var result = AmaisPageTools.SortByPopularity(null);

            Assert.Null(result);
        }

        [Fact]
        public void SortByPopularity_WhenTemplatesEmpty_ReturnsEmpty()
        {
            var result = AmaisPageTools.SortByPopularity([]);

            Assert.Empty(result);
        }

        [Fact]
        public void SortByPopularity_WithMultipleTemplates_ReturnsSortedByPageCountDescending()
        {
            var templates = new List<GetTemplateData>
            {
                new() { TemplateName = "Basic",   PageCount = 5  },
                new() { TemplateName = "Landing", PageCount = 42 },
                new() { TemplateName = "Blog",    PageCount = 18 },
                new() { TemplateName = "Contact", PageCount = 1  },
            };

            var result = AmaisPageTools.SortByPopularity(templates);

            Assert.Equal(["Landing", "Blog", "Basic", "Contact"],
                result.Select(t => t.TemplateName));
        }

        [Fact]
        public void SortByPopularity_WithTiedPageCount_IncludesAllTemplates()
        {
            var templates = new List<GetTemplateData>
            {
                new() { TemplateName = "A", PageCount = 10 },
                new() { TemplateName = "B", PageCount = 10 },
                new() { TemplateName = "C", PageCount = 5  },
            };

            var result = AmaisPageTools.SortByPopularity(templates);

            Assert.Equal(3, result.Count);
            Assert.Equal(10, result[0].PageCount);
            Assert.Equal(10, result[1].PageCount);
            Assert.Equal(5, result[2].PageCount);
        }
    }
}
