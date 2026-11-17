using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateTenantToolFunctions
{
    /// <summary>
    /// Each call to <see cref="Create"/> returns a fresh handler over fresh rows, because the handler mutates
    /// the tracked entities it loads.
    /// </summary>
    public class UpdateTenantToolFunctionsCommandHandlerTesting
    {
        public const int McpToolId = 20;
        public const int HttpToolId = 21;
        public const int PublishedCopyId = 30;

        public sealed record Setup(UpdateTenantToolFunctionsCommandHandler Handler, TenantTool McpTool, PublishedTenantTool PublishedCopy, PublishedTenantTool DeletedCopy, Mock<IMasterDataDbContext> Db);

        public Setup Create()
        {
            var mcpTool = ToolPermissionsTestData.McpTool(McpToolId,
                snapshot: ToolPermissionsTestData.Snapshot(("search", false), ("create", true)),
                enabled: ToolPermissionsTestData.Enabled("search", "removed_one"));

            var publishedCopy = new PublishedTenantTool { Id = PublishedCopyId, Type = "MCP", CreatedByTenantToolId = McpToolId, Tenant = ToolPermissionsTestData.Tenant };
            var deletedCopy = new PublishedTenantTool { Id = PublishedCopyId + 1, Type = "MCP", CreatedByTenantToolId = McpToolId, IsDeleted = true, Tenant = ToolPermissionsTestData.Tenant };

            var tools = new List<TenantTool> { mcpTool, ToolPermissionsTestData.OtherTool(HttpToolId, "HTTP") }.BuildMockDbSet();
            var published = new List<PublishedTenantTool> { publishedCopy, deletedCopy }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tools.Object);
            db.Setup(x => x.PublishedTenantTools).Returns(published.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var handler = new UpdateTenantToolFunctionsCommandHandler(db.Object, new TenantContext { TenantName = ToolPermissionsTestData.TenantName });
            return new Setup(handler, mcpTool, publishedCopy, deletedCopy, db);
        }
    }
}
