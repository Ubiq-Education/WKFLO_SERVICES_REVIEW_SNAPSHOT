using AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Common;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.RateMarketplaceAgent
{
    public class RateMarketplaceAgentCommandTests
    {
        [Fact]
        public void Command_WithValidIdAndRating_ShouldPassValidation()
        {
            // Arrange
            var command = new RateMarketplaceAgentCommand
            {
                Id = 1,
                Rating = 5
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
            var command = new RateMarketplaceAgentCommand
            {
                Id = 0,
                Rating = 3
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.NotEmpty(validationResults);
            Assert.Contains(validationResults, v => v.MemberNames.Contains(nameof(command.Id)) &&
                                                    v.ErrorMessage == ErrorConst.MustBePositive);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        [InlineData(-1)]
        [InlineData(10)]
        public void Command_WithInvalidRating_ShouldFailValidation(int rating)
        {
            // Arrange
            var command = new RateMarketplaceAgentCommand
            {
                Id = 1,
                Rating = rating
            };

            // Act
            var validationResults = ValidateModel(command);

            // Assert
            Assert.NotEmpty(validationResults);
            Assert.Contains(validationResults, v => v.MemberNames.Contains(nameof(command.Rating)) &&
                                                    v.ErrorMessage == ErrorConst.AgentRatingMustBeBetween1And5);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 2)]
        [InlineData(1, 3)]
        [InlineData(1, 4)]
        [InlineData(1, 5)]
        [InlineData(100, 1)]
        [InlineData(999, 5)]
        public void Command_WithValidParameters_ShouldPassValidation(int id, int rating)
        {
            // Arrange
            var command = new RateMarketplaceAgentCommand
            {
                Id = id,
                Rating = rating
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
            var command = new RateMarketplaceAgentCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Mediator.IWkfloRequest<AMAIS.WKFLO.Common.Models.Results.Result>>(command);
        }

        [Fact]
        public void Command_ShouldImplementIComandId()
        {
            // Arrange
            var command = new RateMarketplaceAgentCommand();

            // Act & Assert
            Assert.IsAssignableFrom<AMAIS.WKFLO.Common.Interfaces.Commands.IRequestId<int>>(command);
        }

        [Fact]
        public void Command_Properties_ShouldHaveCorrectTypes()
        {
            // Arrange & Act
            var command = new RateMarketplaceAgentCommand();

            // Assert
            Assert.IsType<int>(command.Id);
            Assert.IsType<int>(command.Rating);
        }

        [Fact]
        public void Command_Id_ShouldHaveRequiredAttribute()
        {
            // Arrange
            var property = typeof(RateMarketplaceAgentCommand).GetProperty(nameof(RateMarketplaceAgentCommand.Id));

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
            var property = typeof(RateMarketplaceAgentCommand).GetProperty(nameof(RateMarketplaceAgentCommand.Id));

            // Act
            var rangeAttribute = property?.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() as RangeAttribute;

            // Assert
            Assert.NotNull(rangeAttribute);
            Assert.Equal(1, rangeAttribute.Minimum);
            Assert.Equal(int.MaxValue, rangeAttribute.Maximum);
            Assert.Equal(ErrorConst.MustBePositive, rangeAttribute.ErrorMessage);
        }

        [Fact]
        public void Command_Rating_ShouldHaveRequiredAttribute()
        {
            // Arrange
            var property = typeof(RateMarketplaceAgentCommand).GetProperty(nameof(RateMarketplaceAgentCommand.Rating));

            // Act
            var requiredAttribute = property?.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault() as RequiredAttribute;

            // Assert
            Assert.NotNull(requiredAttribute);
            Assert.Equal(ErrorConst.Required, requiredAttribute.ErrorMessage);
        }

        [Fact]
        public void Command_Rating_ShouldHaveRangeAttribute()
        {
            // Arrange
            var property = typeof(RateMarketplaceAgentCommand).GetProperty(nameof(RateMarketplaceAgentCommand.Rating));

            // Act
            var rangeAttribute = property?.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() as RangeAttribute;

            // Assert
            Assert.NotNull(rangeAttribute);
            Assert.Equal(1, rangeAttribute.Minimum);
            Assert.Equal(5, rangeAttribute.Maximum);
            Assert.Equal(ErrorConst.AgentRatingMustBeBetween1And5, rangeAttribute.ErrorMessage);
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