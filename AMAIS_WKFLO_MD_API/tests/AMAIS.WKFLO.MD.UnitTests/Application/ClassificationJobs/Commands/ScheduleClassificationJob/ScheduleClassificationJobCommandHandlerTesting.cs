using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Commands.ScheduleClassificationJob;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.ClassificationJobs.Commands.ScheduleClassificationJob
{
    public class ScheduleClassificationJobCommandHandlerTesting
    {
        public ScheduleClassificationJobCommandHandler ScheduleClassificationJobCommandHandler { get; set; }
        public Mock<IActorProxyFactory> ActorProxyFactoryMock { get; set; }
        public Mock<IClassificationJobSchedulerActor> ActorProxyMock { get; set; }

        public ScheduleClassificationJobCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test" };
            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var agents = new List<Agent>
            {
                new TenantAgent { Id = 1, TenantId = 1, Tenant = tenant },
                new TenantAgent { Id = 2, TenantId = 1, Tenant = tenant }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(agents.Object);

            ActorProxyMock = new Mock<IClassificationJobSchedulerActor>();
            ActorProxyMock
                .Setup(x => x.NotifyTopicConfigChangedAsync())
                .Returns(Task.CompletedTask);

            ActorProxyFactoryMock = new Mock<IActorProxyFactory>();
            ActorProxyFactoryMock
                .Setup(x => x.CreateActorProxy<IClassificationJobSchedulerActor>(
                    It.IsAny<ActorId>(),
                    It.IsAny<string>(),
                    It.IsAny<ActorProxyOptions>()))
                .Returns(ActorProxyMock.Object);

            ScheduleClassificationJobCommandHandler = new ScheduleClassificationJobCommandHandler(
                db.Object,
                tenantContext,
                ActorProxyFactoryMock.Object,
                new Mock<ILogger<ScheduleClassificationJobCommandHandler>>().Object
            );
        }
    }
}
