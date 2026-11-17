using AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentContactExists;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using Moq;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.CheckPublicAgentContactExists
{
    public class CheckPublicAgentContactExistsQueryHandlerTesting
    {
        public CheckPublicAgentContactExistsQueryHandler Handler { get; }
        public Mock<IContactFormPluginLogService> ContactFormLogServiceMock { get; }

        public CheckPublicAgentContactExistsQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "test-tenant" };
            ContactFormLogServiceMock = new Mock<IContactFormPluginLogService>();

            Handler = new CheckPublicAgentContactExistsQueryHandler(
                tenantContext,
                ContactFormLogServiceMock.Object
            );
        }
    }
}
