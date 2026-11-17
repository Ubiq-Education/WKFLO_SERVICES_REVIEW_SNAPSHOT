using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommandHandlerTesting
    {
        public UpdateTenantToolCommandHandler UpdateTenantToolCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int TenantToolId { get; set; }


        public UpdateTenantToolCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenantTool = new TenantTool()
            {
                Id = TenantToolId,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "{}",
                IsActive = true,
                CreatedOn = fixedDate
            };

            var tenantTools = new List<TenantTool>() { tenantTool }.BuildMockDbSet();
            var tenants = new List<Tenant>() { }.BuildMockDbSet();
            // Tests share this fixture's single TenantTool, so one test can leave it as an OAuth2 MCP tool with a
            // connection id; a later test that changes its type or credentials then resets that connection's
            // tokens and pending flows, which must be queryable (empty) rather than null.
            var toolOAuthTokens = new List<ToolOAuthToken>().BuildMockDbSet();
            var pendingFlows = new List<McpOAuthPendingFlow>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.ToolOAuthTokens).Returns(toolOAuthTokens.Object);
            db.Setup(x => x.McpOAuthPendingFlows).Returns(pendingFlows.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            var settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" });

            UpdateTenantToolCommandHandler = new UpdateTenantToolCommandHandler
            (
                db.Object,
                mapper,
                settings,
                Mock.Of<IMcpTokenProtector>()
            );
        }
    }
}

