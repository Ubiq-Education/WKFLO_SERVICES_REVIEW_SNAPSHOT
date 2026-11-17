using AMAIS.WKFLO.MD.Application.Tools.Queries.ListMarketplaceTools;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.ListMarketplaceTools
{
    public class ListMarketplaceToolsQueryTests
    {
        [Fact]
        public void ValidateListMarketplaceToolsQuery()
        {
            var command = new ListMarketplaceToolsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
