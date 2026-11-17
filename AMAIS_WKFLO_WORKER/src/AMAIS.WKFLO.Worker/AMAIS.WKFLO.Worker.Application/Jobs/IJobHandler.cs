using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.Worker.Application.Jobs
{
    public interface IJobHandler
    {
        string JobType { get; }
        Task ExecuteAsync(ClassificationJob job, CancellationToken cancellationToken);
    }
}
