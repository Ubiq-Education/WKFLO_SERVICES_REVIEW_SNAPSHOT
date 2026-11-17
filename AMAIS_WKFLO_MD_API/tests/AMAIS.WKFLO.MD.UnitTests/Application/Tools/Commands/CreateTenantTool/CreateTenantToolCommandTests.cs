using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.CreateTenantTool
{
    public class CreateTenantToolCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string TypeTooLong = new('*', 101);

        [Theory]
        [MemberData(nameof(GetTenantToolData))]
        public void ValidateCreateTenantToolCommand(CreateTenantToolData data, bool expected)
        {
            var command = new CreateTenantToolCommand()
            {
                Name = data.Name,
                Description = data.Description,
                Type = data.Type,
                Config = data.Config,
                IsActive = data.IsActive
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetTenantToolData()
        {
            yield return new object[]
            {
                    new CreateTenantToolData() { Name = "Name Test", Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new CreateTenantToolData() { Name = null, Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantToolData() { Name = "Name Test", Description = null, Type = "Type Test", Config = "{}", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantToolData() { Name = "Name Test", Description = "Description Test", Type = null, Config = "{}", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateTenantToolData() {Name = NameTooLong, Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new CreateTenantToolData() {Name = "Name Test", Description = "Description Test", Type = TypeTooLong, Config = "{}", IsActive = true},
                    false
            };
        }
    }

    public class CreateTenantToolData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public bool IsActive { get; set; }
    }
}
