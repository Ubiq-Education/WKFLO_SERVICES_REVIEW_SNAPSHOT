using AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.DeleteTenantTool
{
    public class DeleteTenantToolCommandTests
    {
        [Theory]
        [MemberData(nameof(GetTenantToolData))]
        public void ValidateDeleteTenantToolCommand(DeleteTenantToolData data, bool expected)
        {
            var command = new DeleteTenantToolCommand()
            {
                Id = data.Id
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
                    new DeleteTenantToolData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteTenantToolData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteTenantToolData
    {
        public int Id { get; set; }
    }
}
