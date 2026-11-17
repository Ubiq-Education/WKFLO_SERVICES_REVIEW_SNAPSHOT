using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Tools.Commands.InstallMarketplaceTool;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.InstallMarketplaceAgent
{
    public class InstallMarketplaceAgentCommandHandlerTesting
    {
        public InstallMarketplaceAgentCommandHandler InstallMarketplaceAgentCommandHandler { get; set; }
        public Mock<IDateTimeProvider> DateTimeProviderMock { get; set; }
        public int MarketplaceAgentId { get; set; } = 1;
        public int TenantId { get; set; } = 1;
        public string TenantName { get; set; } = "Test";

        public InstallMarketplaceAgentCommandHandlerTesting()
        {
            var tenantContext = new TenantContext() { TenantName = TenantName };

            var mapper = WkfloMapperExtensions.CreateMapper();
            // Setup mocked DateTimeProvider
            DateTimeProviderMock = new Mock<IDateTimeProvider>();
            var fixedDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            DateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(fixedDate);

            var tenant = new Tenant()
            {
                Id = TenantId,
                Name = TenantName,
                Region = "UK"
            };

            var aiModel = new AIModel()
            {
                Id = 1,
                Name = "GPT-4",
                Provider = "OpenAI"
            };

            var marketplaceTool = new MarketplaceTool()
            {
                Id = 1,
                Name = "Test Tool",
                Description = "Test Tool Description",
                Type = "API",
                Config = "{}",
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var publishedTenantTool = new PublishedTenantTool()
            {
                Id = 1,
                Name = "Test Tool",
                Description = "Test Tool Description",
                Type = "API",
                Config = "{}",
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var marketplaceAgent = new MarketplaceAgent()
            {
                Id = MarketplaceAgentId,
                Name = "Test Agent",
                Description = "Test Agent Description",
                SystemPrompt = "Test System Prompt",
                AIModelId = 1,
                AIModel = aiModel,
                AgentTools = new List<AgentTool>
                {
                    new AgentTool
                    {
                        AgentId = MarketplaceAgentId,
                        ToolId = 1,
                        Tool = publishedTenantTool
                    }
                },
                CreatedOn = fixedDate,
                IsDeleted = false
            };

            var marketplaceAgents = new List<MarketplaceAgent>() { marketplaceAgent }.BuildMockDbSet();
            var tenants = new List<Tenant>() { tenant }.BuildMockDbSet();
            var tenantAgents = new List<TenantAgent>().BuildMockDbSet();
            var agentTools = new List<AgentTool>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.AgentTools).Returns(agentTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            var mediator = new Mock<IWkfloMediator>();
            mediator.Setup(m => m.Send(It.IsAny<InstallMarketplaceToolCommand>(), TestContext.Current.CancellationToken))
                .ReturnsAsync(new NotFoundResult());

            InstallMarketplaceAgentCommandHandler = new InstallMarketplaceAgentCommandHandler(
                db.Object,
                mapper,
                mediator.Object,
                tenantContext
            );
        }
    }
}
