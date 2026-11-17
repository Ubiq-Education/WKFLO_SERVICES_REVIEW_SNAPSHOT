using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.DeleteTenantAgent
{
    public class DeleteTenantAgentCommandTests
    {
        [Theory]
        [MemberData(nameof(GetTenantAgentData))]
        public void ValidateDeleteTenantAgentCommand(DeleteTenantAgentData data, bool expected)
        {
            var command = new DeleteTenantAgentCommand()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetTenantAgentData()
        {
            yield return new object[]
            {
                    new DeleteTenantAgentData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteTenantAgentData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteTenantAgentData
    {
        public int Id { get; set; }
    }
}
