using Dapr.Actors;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors
{
    public interface IClassificationJobSchedulerActor : IActor
    {
        Task NotifyTopicConfigChangedAsync();
    }
}
