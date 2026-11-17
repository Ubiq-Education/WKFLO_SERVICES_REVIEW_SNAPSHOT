using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// WI 7575: started by a decision endpoint once a decision is recorded; runs in the background with the
    /// decision request's contexts (see <see cref="BackgroundToolApprovalResumeQueue"/>).
    /// </summary>
    public class ResumeToolApprovalCommand : IWkfloRequest<Result>
    {
        public string ApprovalId { get; set; }

        public string SessionId { get; set; }
    }
}
