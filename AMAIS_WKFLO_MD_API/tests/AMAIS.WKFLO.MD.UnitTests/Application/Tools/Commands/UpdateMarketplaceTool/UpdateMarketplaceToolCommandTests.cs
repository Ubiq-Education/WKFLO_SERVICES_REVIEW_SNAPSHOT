using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateMarketplaceTool
{
    public class UpdateMarketplaceToolCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string TypeTooLong = new('*', 101);

        [Theory]
        [MemberData(nameof(GetMarketplaceToolData))]
        public void ValidateUpdateMarketplaceToolCommand(UpdateMarketplaceToolData data, bool expected)
        {
            var command = new UpdateMarketplaceToolCommand()
            {
                Id = 1,
                Name = data.Name,
                Description = data.Description,
                Type = data.Type,
                Config = data.Config
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
                    new UpdateMarketplaceToolData() { Name = "Name Test", Description = "Description Test", Type = "Type Test", Config = "{}" },
                    true
            };

            yield return new object[]
            {
                    new UpdateMarketplaceToolData() { Name = null, Description = "Description Test", Type = "Type Test", Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new UpdateMarketplaceToolData() { Name = "Name Test", Description = null, Type = "Type Test", Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new UpdateMarketplaceToolData() { Name = "Name Test", Description = "Description Test", Type = null, Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new UpdateMarketplaceToolData() {Name = NameTooLong, Description = "Description Test", Type = "Type Test", Config = "{}"},
                    false
            };

            yield return new object[]
            {
                    new UpdateMarketplaceToolData() {Name = "Name Test", Description = "Description Test", Type = TypeTooLong, Config = "{}"},
                    false
            };
        }
    }

    public class UpdateMarketplaceToolData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
    }
}
