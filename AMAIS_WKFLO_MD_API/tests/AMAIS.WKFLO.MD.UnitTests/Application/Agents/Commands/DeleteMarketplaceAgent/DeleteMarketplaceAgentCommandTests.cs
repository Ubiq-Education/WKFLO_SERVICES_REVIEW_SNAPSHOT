using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteMarketplaceAgent
{
    public class DeleteMarketplaceAgentCommandTests
    {
        [Theory]
        [MemberData(nameof(GetMarketplaceAgentData))]
        public void ValidateDeleteMarketplaceAgentCommand(DeleteMarketplaceAgentData data, bool expected)
        {
            var command = new DeleteMarketplaceAgentCommand()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetMarketplaceAgentData()
        {
            yield return new object[]
            {
                    new DeleteMarketplaceAgentData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteMarketplaceAgentData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteMarketplaceAgentData
    {
        public int Id { get; set; }
    }
}
