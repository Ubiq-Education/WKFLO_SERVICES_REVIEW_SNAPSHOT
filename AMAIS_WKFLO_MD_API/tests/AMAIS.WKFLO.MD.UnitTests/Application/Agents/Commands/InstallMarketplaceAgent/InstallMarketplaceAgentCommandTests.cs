using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Common;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.InstallMarketplaceAgent
{
    public class InstallMarketplaceAgentCommandTests
    {
        [Fact]
        public void Command_WithValidId_ShouldPassValidation()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = 1
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.Empty(validationResults);
        }

        [Fact]
        public void Command_WithDefaultId_ShouldFailValidation()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = 0
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.NotEmpty(validationResults);
            Assert.Contains(validationResults, v => v.MemberNames.Contains(nameof(command.Id)) &&
                                                    v.ErrorMessage == ErrorConst.MustBePositive);
        }

        [Fact]
        public void Command_WithoutSettingId_ShouldFailValidation()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand();

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.NotEmpty(validationResults);
            Assert.Contains(validationResults, v => v.MemberNames.Contains(nameof(command.Id)) &&
                                                    v.ErrorMessage == ErrorConst.MustBePositive);
        }

        [Fact]
        public void Command_Properties_ShouldHaveCorrectTypes()
        {
            // Arrange & Act
            var command = new InstallMarketplaceAgentCommand();

            // Assert
            Assert.IsType<int>(command.Id);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(999)]
        [InlineData(int.MaxValue)]
        public void Command_WithValidPositiveIds_ShouldPassValidation(int id)
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = id
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.Empty(validationResults);
        }

        [Fact]
        public void Command_ShouldImplementIRequest()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Mediator.IWkfloRequest<AMAIS.WKFLO.Common.Models.Results.Result>>(command);
        }

        [Fact]
        public void Command_ShouldImplementIComandId()
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Interfaces.Commands.IRequestId<int>>(command);
        }

        [Fact]
        public void Command_Id_ShouldHaveRequiredAttribute()
        {
            // Arrange
            var property = typeof(InstallMarketplaceAgentCommand).GetProperty(nameof(InstallMarketplaceAgentCommand.Id));

            // Act
            var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault() as RequiredAttribute;

            // Assert
            Assert.NotNull(requiredAttribute);
            Assert.Equal(ErrorConst.Required, requiredAttribute.ErrorMessage);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public void Command_WithNegativeIds_ShouldFailValidation(int id)
        {
            // Arrange
            var command = new InstallMarketplaceAgentCommand
            {
                Id = id
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.NotEmpty(validationResults);
            Assert.Contains(validationResults, v => v.MemberNames.Contains(nameof(command.Id)) &&
                                                    v.ErrorMessage == ErrorConst.MustBePositive);
        }

        private static List<ValidationResult> ValidateModel(object model)
        {
            var validationResults = new List<ValidationResult>();
            var context = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, context, validationResults, true);
            return validationResults;
        }
    }
}