using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Queries.GetClassificationJobStatus;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.ClassificationJobs.Queries.GetClassificationJobStatus
{
    public class GetClassificationJobStatusQueryHandlerTesting
    {
        public GetClassificationJobStatusQueryHandler GetClassificationJobStatusQueryHandler { get; set; }

        public GetClassificationJobStatusQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = "Test" };

            var tenant = new Tenant { Id = 1, Name = tenantContext.TenantName };

            var jobs = new List<ClassificationJob>
            {
                new()
                {
                    Id = 1,
                    AgentId = 1,
                    TenantId = 1,
                    Tenant = tenant,
                    JobType = CommonJobTypeConsts.TopicClassification,
                    PreparedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)
                },
                new()
                {
                    Id = 2,
                    AgentId = 2,
                    TenantId = 1,
                    Tenant = tenant,
                    JobType = CommonJobTypeConsts.TopicClassification,
                    PreparedAt = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero),
                    QueuedAt = new DateTimeOffset(2026, 1, 2, 18, 0, 0, TimeSpan.Zero),
                    ProcessingStartAt = new DateTimeOffset(2026, 1, 2, 18, 1, 0, TimeSpan.Zero),
                    FinishedAt = new DateTimeOffset(2026, 1, 2, 19, 0, 0, TimeSpan.Zero),
                    IsSuccess = true,
                    AIModelName = "gpt-4o",
                    TotalInputTokens = 5000,
                    TotalOutputTokens = 200
                }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.ClassificationJobs).Returns(jobs.Object);

            GetClassificationJobStatusQueryHandler = new GetClassificationJobStatusQueryHandler(
                db.Object,
                tenantContext
            );
        }
    }
}
