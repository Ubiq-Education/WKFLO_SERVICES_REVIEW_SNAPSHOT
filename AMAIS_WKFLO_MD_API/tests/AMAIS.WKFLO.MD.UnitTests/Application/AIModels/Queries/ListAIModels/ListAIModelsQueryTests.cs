using AMAIS.WKFLO.MD.Application.AIModels.Queries.ListAIModels;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Queries.ListAIModels
{
    public class ListAIModelsQueryTests
    {
        [Fact]
        public void ValidateListAIModelsQuery()
        {
            var command = new ListAIModelsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
