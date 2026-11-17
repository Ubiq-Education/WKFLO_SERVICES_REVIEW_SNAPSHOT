using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgents;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.ListTenantAgents
{
    public class ListTenantAgentsQueryTests
    {
        [Fact]
        public void ValidateListTenantAgentsQuery()
        {
            var command = new ListTenantAgentsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
