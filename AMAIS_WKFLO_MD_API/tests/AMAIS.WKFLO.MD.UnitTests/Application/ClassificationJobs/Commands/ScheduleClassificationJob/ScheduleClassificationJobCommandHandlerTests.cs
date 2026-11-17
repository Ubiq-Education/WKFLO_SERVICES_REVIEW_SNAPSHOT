using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Commands.ScheduleClassificationJob;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.ClassificationJobs.Commands.ScheduleClassificationJob
{
    public class ScheduleClassificationJobCommandHandlerTests : IClassFixture<ScheduleClassificationJobCommandHandlerTesting>
    {
        private readonly ScheduleClassificationJobCommandHandlerTesting _testing;

        public ScheduleClassificationJobCommandHandlerTests(ScheduleClassificationJobCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Handle_WithExistingAgent_ShouldReturnNoContent(int agentId)
        {
            // Act
            var result = await _testing.ScheduleClassificationJobCommandHandler.Handle(
                new ScheduleClassificationJobCommand { AgentId = agentId },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NoContent);
            _testing.ActorProxyMock.Verify(x => x.NotifyTopicConfigChangedAsync(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Handle_WithNonExistentAgent_ShouldReturnNotFound()
        {
            // Act
            var result = await _testing.ScheduleClassificationJobCommandHandler.Handle(
                new ScheduleClassificationJobCommand { AgentId = 999 },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
        }

        [Fact]
        public async Task Handle_WhenActorThrows_ShouldReturnUnexpected()
        {
            var tenant = new Tenant { Id = 1, Name = "Test" };
            var agents = new List<Agent>
            {
                new TenantAgent { Id = 1, TenantId = 1, Tenant = tenant }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(agents.Object);

            var failingActor = new Mock<IClassificationJobSchedulerActor>();
            failingActor
                .Setup(x => x.NotifyTopicConfigChangedAsync())
                .ThrowsAsync(new InvalidOperationException("Dapr actor unavailable"));

            var failingFactory = new Mock<IActorProxyFactory>();
            failingFactory
                .Setup(x => x.CreateActorProxy<IClassificationJobSchedulerActor>(
                    It.IsAny<ActorId>(),
                    It.IsAny<string>(),
                    It.IsAny<ActorProxyOptions>()))
                .Returns(failingActor.Object);

            var handler = new ScheduleClassificationJobCommandHandler(
                db.Object,
                new TenantContext { TenantName = "Test" },
                failingFactory.Object,
                new Mock<ILogger<ScheduleClassificationJobCommandHandler>>().Object);

            // Act
            var result = await handler.Handle(
                new ScheduleClassificationJobCommand { AgentId = 1 },
                TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Unexpected);
        }
    }
}
