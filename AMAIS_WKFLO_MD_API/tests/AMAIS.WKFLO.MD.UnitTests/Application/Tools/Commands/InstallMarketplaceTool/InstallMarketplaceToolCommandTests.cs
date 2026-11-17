using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.InstallMarketplaceTool
{
    public class InstallMarketplaceToolCommandTests
    {
        [Fact]
        public void Command_WithValidId_ShouldPassValidation()
        {
            // Arrange
            var command = new InstallMarketplaceToolCommand
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
            var command = new InstallMarketplaceToolCommand
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
            var command = new InstallMarketplaceToolCommand();

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
            var command = new InstallMarketplaceToolCommand();

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
            var command = new InstallMarketplaceToolCommand
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
            var command = new InstallMarketplaceToolCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Mediator.IWkfloRequest<AMAIS.WKFLO.Common.Models.Results.Result>>(command);
        }

        [Fact]
        public void Command_ShouldImplementIComandId()
        {
            // Arrange
            var command = new InstallMarketplaceToolCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Interfaces.Commands.IRequestId<int>>(command);
        }

        [Fact]
        public void Command_Id_ShouldHaveRequiredAttribute()
        {
            // Arrange
            var property = typeof(InstallMarketplaceToolCommand).GetProperty(nameof(InstallMarketplaceToolCommand.Id));

            // Act
            var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault() as RequiredAttribute;

            // Assert
            Assert.NotNull(requiredAttribute);
            Assert.Equal(ErrorConst.Required, requiredAttribute.ErrorMessage);
        }

        [Fact]
        public void Command_Id_ShouldHaveRangeAttribute()
        {
            // Arrange
            var property = typeof(InstallMarketplaceToolCommand).GetProperty(nameof(InstallMarketplaceToolCommand.Id));

            // Act
            var rangeAttribute = property?.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() as RangeAttribute;

            // Assert
            Assert.NotNull(rangeAttribute);
            Assert.Equal(1, rangeAttribute.Minimum);
            Assert.Equal(int.MaxValue, rangeAttribute.Maximum);
            Assert.Equal(ErrorConst.MustBePositive, rangeAttribute.ErrorMessage);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public void Command_WithNegativeIds_ShouldFailValidation(int id)
        {
            // Arrange
            var command = new InstallMarketplaceToolCommand
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

        [Fact]
        public void Command_DefaultConstructor_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var command = new InstallMarketplaceToolCommand();

            // Assert
            Assert.Equal(0, command.Id); // Default int value
        }

        [Fact]
        public void Command_ShouldAllowSettingId()
        {
            // Arrange
            var command = new InstallMarketplaceToolCommand();
            const int expectedId = 42;

            // Act
            command.Id = expectedId;

            // Assert
            Assert.Equal(expectedId, command.Id);
        }

        [Fact]
        public void Command_MultipleInstances_ShouldBeIndependent()
        {
            // Arrange
            var command1 = new InstallMarketplaceToolCommand { Id = 1 };
            var command2 = new InstallMarketplaceToolCommand { Id = 2 };

            // Act & Assert
            Assert.Equal(1, command1.Id);
            Assert.Equal(2, command2.Id);
            Assert.NotEqual(command1.Id, command2.Id);
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