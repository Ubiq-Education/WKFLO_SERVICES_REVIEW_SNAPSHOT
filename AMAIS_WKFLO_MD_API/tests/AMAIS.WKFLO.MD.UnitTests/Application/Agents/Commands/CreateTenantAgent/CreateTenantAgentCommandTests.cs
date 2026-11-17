using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateTenantAgent
{
    public class CreateTenantAgentCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string DescriptionTooLong = new('*', 501);

        [Theory]
        [MemberData(nameof(GetTenantAgentData))]
        public void ValidateCreateTenantAgentCommand(CreateTenantAgentData data, bool expected)
        {
            var command = new CreateTenantAgentCommand()
            {
                Name = data.Name,
                Description = data.Description,
                SystemPrompt = data.SystemPrompt,
                ResponseTone = data.ResponseTone,
                IsActive = data.IsActive
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
                    new CreateTenantAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new CreateTenantAgentData() { Name = null, Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantAgentData() { Name = "Name Test", Description = null, SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = null, ResponseTone = "Friendly", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantAgentData() {Name = NameTooLong, Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new CreateTenantAgentData() {Name = "Name Test", Description = DescriptionTooLong, SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true},
                    false
            };
        }
    }

    public class CreateTenantAgentData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseTone { get; set; }
        public bool IsActive { get; set; }
    }
}
