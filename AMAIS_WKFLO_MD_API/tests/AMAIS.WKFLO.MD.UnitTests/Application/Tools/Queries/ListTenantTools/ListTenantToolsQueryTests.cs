using AMAIS.WKFLO.MD.Application.Tools.Queries.ListTenantTools;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Queries.ListTenantTools
{
    public class ListTenantToolsQueryTests
    {
        [Fact]
        public void ValidateListTenantToolsQuery()
        {
            var command = new ListTenantToolsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
