using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string TypeTooLong = new('*', 101);

        [Theory]
        [MemberData(nameof(GetMarketplaceToolData))]
        public void ValidateCreateMarketplaceToolCommand(CreateMarketplaceToolData data, bool expected)
        {
            var command = new CreateMarketplaceToolCommand()
            {
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
                    new CreateMarketplaceToolData() { Name = "Name Test", Description = "Description Test", Type = "Type Test", Config = "{}" },
                    true
            };

            yield return new object[]
            {
                    new CreateMarketplaceToolData() { Name = null, Description = "Description Test", Type = "Type Test", Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceToolData() { Name = "Name Test", Description = null, Type = "Type Test", Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceToolData() { Name = "Name Test", Description = "Description Test", Type = null, Config = "{}"  },
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceToolData() {Name = NameTooLong, Description = "Description Test", Type = "Type Test", Config = "{}"},
                    false
            };

            yield return new object[]
            {
                    new CreateMarketplaceToolData() {Name = "Name Test", Description = "Description Test", Type = TypeTooLong, Config = "{}"},
                    false
            };
        }
    }

    public class CreateMarketplaceToolData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
    }
}
