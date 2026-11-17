using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateMarketplaceAgent
{
    public class CreateMarketplaceAgentCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string DescriptionTooLong = new('*', 501);

        [Theory]
        [MemberData(nameof(GetMarketplaceAgentData))]
        public void ValidateCreateMarketplaceAgentCommand(CreateMarketplaceAgentData data, bool expected)
        {
            var command = new CreateMarketplaceAgentCommand()
            {
                Name = data.Name,
                Description = data.Description,
                SystemPrompt = data.SystemPrompt
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
                    new CreateMarketplaceAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = "SystemPrompt Test", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new CreateMarketplaceAgentData() { Name = null, Description = "Description Test", SystemPrompt = "SystemPrompt Test", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceAgentData() { Name = "Name Test", Description = null, SystemPrompt = "SystemPrompt Test", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = null, IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceAgentData() {Name = NameTooLong, Description = "Description Test", SystemPrompt = "SystemPrompt Test", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceAgentData() {Name = "Name Test", Description = DescriptionTooLong, SystemPrompt = "SystemPrompt Test", IsActive = true},
                    false
            };
        }
    }

    public class CreateMarketplaceAgentData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public bool IsActive { get; set; }
    }
}
