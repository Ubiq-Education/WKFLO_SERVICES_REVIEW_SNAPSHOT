using Dapr.Actors.Runtime;

namespace AMAIS.WKFLO.AI.Application.Common.Activities
{
    public class WorkflowActor : Actor, IWorkflowActor
    {
        public WorkflowActor(ActorHost host) : base(host)
        {
        }
    }
}
