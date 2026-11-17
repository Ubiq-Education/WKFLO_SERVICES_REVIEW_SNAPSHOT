using AMAIS.WKFLO.MD.Application.Secrets.Queries.ListSecrets;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.ListSecrets
{
    public class ListSecretsQueryTests
    {
        [Fact]
        public void ValidateListSecretsQuery()
        {
            var command = new ListSecretsQuery();
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.True(isValid);
        }
    }
}
