using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.DeleteMarketplaceTool
{
    public class DeleteMarketplaceToolCommandTests
    {
        [Theory]
        [MemberData(nameof(GetMarketplaceToolData))]
        public void ValidateDeleteMarketplaceToolCommand(DeleteMarketplaceToolData data, bool expected)
        {
            var command = new DeleteMarketplaceToolCommand()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetMarketplaceToolData()
        {
            yield return new object[]
            {
                    new DeleteMarketplaceToolData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteMarketplaceToolData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteMarketplaceToolData
    {
        public int Id { get; set; }
    }
}
