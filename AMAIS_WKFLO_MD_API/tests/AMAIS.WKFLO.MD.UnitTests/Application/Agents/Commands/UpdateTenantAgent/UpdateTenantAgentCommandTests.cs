using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateTenantAgent
{
    public class UpdateTenantAgentCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string DescriptionTooLong = new('*', 501);

        [Theory]
        [MemberData(nameof(GetTenantAgentData))]
        public void ValidateUpdateTenantAgentCommand(UpdateTenantAgentData data, bool expected)
        {
            var command = new UpdateTenantAgentCommand()
            {
                Id = 1,
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

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(2000, true)]
        [InlineData(2001, false)]
        public void ValidateAdditionalInstructionsMaxLength(object additionalInstructions, bool expected)
        {
            var command = new UpdateTenantAgentCommand()
            {
                Id = 1,
                Name = "Name Test",
                Description = "Description Test",
                SystemPrompt = "SystemPrompt Test",
                AdditionalInstructions = additionalInstructions is int length ? new string('a', length) : (string)additionalInstructions
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
                    new UpdateTenantAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true },
                    true
             };

            yield return new object[]
            {
                    new UpdateTenantAgentData() { Name = null, Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantAgentData() { Name = "Name Test", Description = null, SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantAgentData() { Name = "Name Test", Description = "Description Test", SystemPrompt = null, ResponseTone = "Friendly", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantAgentData() {Name = NameTooLong, Description = "Description Test", SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantAgentData() {Name = "Name Test", Description = DescriptionTooLong, SystemPrompt = "SystemPrompt Test", ResponseTone = "Friendly", IsActive = true},
                    false
            };
        }
    }

    public class UpdateTenantAgentData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseTone { get; set; }
        public bool IsActive { get; set; }
    }
}
