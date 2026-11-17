using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateTenantToolFunctions
{
    /// <summary>WI 7726: validation of the optional friendly display values.</summary>
    public class UpdateTenantToolFunctionsCommandValidatorTests
    {
        private const int ToolId = 20;

        private readonly UpdateTenantToolFunctionsCommandValidator _validator;

        public UpdateTenantToolFunctionsCommandValidatorTests()
        {
            var tenants = new List<Tenant> { ToolPermissionsTestData.Tenant }.BuildMockDbSet();
            var tools = new List<TenantTool> { ToolPermissionsTestData.McpTool(ToolId) }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.TenantTools).Returns(tools.Object);

            _validator = new UpdateTenantToolFunctionsCommandValidator(db.Object, new TenantContext { TenantName = ToolPermissionsTestData.TenantName });
        }

        private static UpdateTenantToolFunctionsCommand Command(params UpdateTenantToolFunctionDisplayInput[] functions) => new()
        {
            Id = ToolId,
            EnabledFunctions = ["search"],
            Functions = [.. functions]
        };

        [Fact]
        public void Validate_WithoutFunctions_IsValid()
        {
            Assert.True(_validator.Validate(new UpdateTenantToolFunctionsCommand { Id = ToolId, EnabledFunctions = ["search"] }).IsValid);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "   ")]
        [InlineData("Search content", "Finds content")]
        public void Validate_ValidDisplayValues_IsValid(string displayName, string displayDescription)
        {
            var result = _validator.Validate(Command(new UpdateTenantToolFunctionDisplayInput { Name = "search", DisplayName = displayName, DisplayDescription = displayDescription }));

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_LengthLimitsApplyAfterTrimming()
        {
            var result = _validator.Validate(Command(new UpdateTenantToolFunctionDisplayInput
            {
                Name = "search",
                DisplayName = "  " + new string('n', 100) + "  ",
                DisplayDescription = "  " + new string('d', 500) + "  "
            }));

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(101, 0)]
        [InlineData(0, 501)]
        public void Validate_TooLongDisplayValue_ReturnsMaximumLength(int nameLength, int descriptionLength)
        {
            var result = _validator.Validate(Command(new UpdateTenantToolFunctionDisplayInput
            {
                Name = "search",
                DisplayName = new string('n', nameLength),
                DisplayDescription = new string('d', descriptionLength)
            }));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.MaximumLength);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Validate_MissingFunctionName_ReturnsRequired(string name)
        {
            var result = _validator.Validate(Command(new UpdateTenantToolFunctionDisplayInput { Name = name, DisplayName = "Friendly" }));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.Required);
        }

        [Fact]
        public void Validate_NullFunctionEntry_ReturnsInvalidInput()
        {
            var result = _validator.Validate(new UpdateTenantToolFunctionsCommand { Id = ToolId, EnabledFunctions = ["search"], Functions = [null] });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.InvalidInput);
        }

        [Fact]
        public void Validate_DuplicateFunctionName_ReturnsInvalidInput()
        {
            var result = _validator.Validate(Command(
                new UpdateTenantToolFunctionDisplayInput { Name = "search", DisplayName = "One" },
                new UpdateTenantToolFunctionDisplayInput { Name = "search", DisplayName = "Two" }));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.InvalidInput);
        }
    }
}
