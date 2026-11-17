using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolFunctions;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetTenantToolFunctions
{
    public class GetTenantToolFunctionsQueryHandlerTesting
    {
        public const int McpToolId = 10;
        public const int HttpToolId = 11;

        public GetTenantToolFunctionsQueryHandler Handler { get; }

        public GetTenantToolFunctionsQueryHandlerTesting()
        {
            var tools = new List<TenantTool>
            {
                ToolPermissionsTestData.McpTool(McpToolId,
                    snapshot: ToolPermissionsTestData.Snapshot(("search", false), ("create", true)),
                    enabled: ToolPermissionsTestData.Enabled("search")),
                ToolPermissionsTestData.OtherTool(HttpToolId, "HTTP")
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tools.Object);

            Handler = new GetTenantToolFunctionsQueryHandler(db.Object, new TenantContext { TenantName = ToolPermissionsTestData.TenantName });
        }
    }
}
