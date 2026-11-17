using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval
{
    /// <summary>WI 7575: body of both tool approval decision endpoints.</summary>
    public abstract class DecideToolApprovalCommandBase : IWkfloRequest<Result>
    {
        /// <summary>Set from the route.</summary>
        [JsonIgnore]
        public string ApprovalId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        /// <summary>AllowOnce | AllowForConversation | Deny.</summary>
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Decision { get; set; }
    }

    /// <summary>WI 7575: decision from the authenticated tenant chat (admin test chat) - <c>tenant/agent/{agentId}/tool-approval/{approvalId}</c>.</summary>
    public class DecideTenantToolApprovalCommand : DecideToolApprovalCommandBase, IRequestId<int>
    {
        /// <summary>Tenant agent id, set from the route.</summary>
        [JsonIgnore]
        public int Id { get; set; }
    }

    /// <summary>WI 7575: decision from a shared/public chat - <c>agent/{shareId}/tool-approval/{approvalId}</c>.</summary>
    public class DecidePublicToolApprovalCommand : DecideToolApprovalCommandBase, IRequestShareId<string>
    {
        /// <summary>Set from the route.</summary>
        [JsonIgnore]
        public string ShareId { get; set; }
    }

    /// <summary>Response of a recorded decision.</summary>
    public class ToolApprovalDecisionResult
    {
        public string ApprovalId { get; set; }
        public string Decision { get; set; }

        /// <summary>Approved | Denied.</summary>
        public string Status { get; set; }
    }
}
