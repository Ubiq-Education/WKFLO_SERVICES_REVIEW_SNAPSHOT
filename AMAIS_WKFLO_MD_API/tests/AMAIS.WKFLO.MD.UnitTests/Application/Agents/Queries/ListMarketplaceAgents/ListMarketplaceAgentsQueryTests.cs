using AMAIS.WKFLO.MD.Application.Agents.Queries.ListMarketplaceAgents;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListMarketplaceAgents
{
    public class ListMarketplaceAgentsQueryTests
    {
        [Fact]
        public void ValidateListMarketplaceAgentsQuery()
        {
            var command = new ListMarketplaceAgentsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
