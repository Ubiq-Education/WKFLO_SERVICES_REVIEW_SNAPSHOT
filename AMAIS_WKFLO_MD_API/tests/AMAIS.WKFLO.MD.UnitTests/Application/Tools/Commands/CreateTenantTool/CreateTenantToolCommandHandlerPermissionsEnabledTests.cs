using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.CreateTenantTool
{
    /// <summary>WI 7575: tool permissions are opt-in - a new tool's PermissionsEnabled is false unless the caller sends true, whatever its type.</summary>
    public class CreateTenantToolCommandHandlerPermissionsEnabledTests : IClassFixture<CreateTenantToolCommandHandlerTesting>
    {
        private readonly CreateTenantToolCommandHandlerTesting _testing;

        public CreateTenantToolCommandHandlerPermissionsEnabledTests(CreateTenantToolCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(CommonToolTypeConsts.ChatClassification, null, false)]
        [InlineData(CommonToolTypeConsts.ContactForm, null, false)]
        [InlineData(CommonToolTypeConsts.MCP, null, false)]
        [InlineData(CommonToolTypeConsts.Http, null, false)]
        [InlineData(CommonToolTypeConsts.IntegrationAmais, null, false)]
        [InlineData(CommonToolTypeConsts.ContactForm, true, true)]
        [InlineData(CommonToolTypeConsts.MCP, true, true)]
        [InlineData(CommonToolTypeConsts.Http, false, false)]
        public async Task Handle_PermissionsEnabledIsFalseUnlessExplicitlyTrue(string type, bool? requested, bool expected)
        {
            var result = await _testing.CreateTenantToolCommandHandler.Handle(new CreateTenantToolCommand
            {
                Name = "Tool",
                Description = "Tool description",
                Type = type,
                Config = "{}",
                IsActive = true,
                PermissionsEnabled = requested
            }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            Assert.Equal(expected, success.Data.PermissionsEnabled);
        }
    }
}
