using AMAIS.WKFLO.AI.Application.Agents.Commands.SaveStoppedQueuedTurn;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.SaveStoppedQueuedTurn
{
    /// <summary>US 7502 (N3-1): a turn stopped while still queued finds its agent in the current tenant and saves its marker.</summary>
    public class SaveStoppedQueuedTurnCommandHandlerTests
    {
        private const string TenantName = "tenant-a";

        private readonly Mock<ISemanticKernelAgentService> _service = new();
        private readonly SaveStoppedQueuedTurnCommandHandler _handler;

        public SaveStoppedQueuedTurnCommandHandlerTests()
        {
            var tenant = new Tenant { Name = TenantName };
            var otherTenant = new Tenant { Name = "tenant-b" };
            var maisy = new TenantAgent { Id = 5, Name = "MAISY", Tenant = tenant };
            var published = new PublishedTenantAgent { Id = 6, Name = "MAISY public", Tenant = tenant };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(new List<Agent>
            {
                maisy,
                published,
                new TenantAgent { Id = 9, Name = "Other tenant agent", Tenant = otherTenant }
            }.BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { ShareId = "share-1", AgentId = 6, Agent = published, IsWidget = true }
            }.BuildMockDbSet().Object);

            _handler = new SaveStoppedQueuedTurnCommandHandler(db.Object, new TenantContext { TenantName = TenantName }, _service.Object);
        }

        [Fact]
        public async Task TenantChat_SavesTheMarkerForTheChatAgent()
        {
            var result = await _handler.Handle(new SaveStoppedQueuedTurnCommand { AgentId = 5, SessionId = "session-1" },
                TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<bool>>(result);
            _service.Verify(x => x.SaveTurnStoppedBeforeRunAsync(It.Is<RunnableAgent>(a => a.Id == 5), "session-1", null), Times.Once);
        }

        [Fact]
        public async Task PublicChat_FindsTheAgentFromTheShare_WithItsWidgetContext()
        {
            await _handler.Handle(new SaveStoppedQueuedTurnCommand { ShareId = "share-1", SessionId = "session-1" },
                TestContext.Current.CancellationToken);

            _service.Verify(x => x.SaveTurnStoppedBeforeRunAsync(It.Is<RunnableAgent>(a => a.Id == 6), "session-1", true), Times.Once);
        }

        [Fact]
        public async Task AgentOfAnotherTenant_SavesNothing()
        {
            var result = await _handler.Handle(new SaveStoppedQueuedTurnCommand { AgentId = 9, SessionId = "session-1" },
                TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
            _service.Verify(x => x.SaveTurnStoppedBeforeRunAsync(It.IsAny<RunnableAgent>(), It.IsAny<string>(), It.IsAny<bool?>()), Times.Never);
        }
    }
}
