using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Queries.GetClassificationJobStatus
{
    public class GetClassificationJobStatusQueryHandler : IRequestHandler<GetClassificationJobStatusQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetClassificationJobStatusQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetClassificationJobStatusQuery query, CancellationToken cancellationToken)
        {
            var job = await _db.ClassificationJobs.AsNoTracking()
            .Where(j => j.AgentId == query.AgentId
                && j.Tenant.Name == _tenantContext.TenantName
                && j.JobType == CommonJobTypeConsts.TopicClassification)
            .OrderByDescending(j => j.PreparedAt)
            .FirstOrDefaultAsync(cancellationToken);

            if (job == null)
                return new NotFoundResult(nameof(ClassificationJob), "No classification job found for this agent");

            return new SuccessResult<ClassificationJobStatusResult>(MapToResult(job));
        }

        private static ClassificationJobStatusResult MapToResult(ClassificationJob job) => new()
        {
            Id = job.Id,
            JobType = job.JobType,
            Status = DeriveStatus(job),
            PreparedAt = job.PreparedAt,
            QueuedAt = job.QueuedAt,
            ProcessingStartAt = job.ProcessingStartAt,
            FinishedAt = job.FinishedAt,
            IsSuccess = job.IsSuccess,
            AIModelName = job.AIModelName,
            TotalInputTokens = job.TotalInputTokens,
            TotalOutputTokens = job.TotalOutputTokens,
            TotalConversations = job.TotalConversations,
            TotalMessages = job.TotalMessages,
            ErrorMessage = job.ErrorMessage
        };

        private static string DeriveStatus(ClassificationJob job)
        {
            if (job.FinishedAt.HasValue) return "Done";
            if (job.ProcessingStartAt.HasValue) return "Processing";
            if (job.QueuedAt.HasValue) return "Queued";
            return "Pending";
        }
    }
}
