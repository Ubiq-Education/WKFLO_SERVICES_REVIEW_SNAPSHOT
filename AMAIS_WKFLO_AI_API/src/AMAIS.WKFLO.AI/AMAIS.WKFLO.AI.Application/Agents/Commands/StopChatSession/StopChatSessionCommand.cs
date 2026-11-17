using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7501: body of both Stop endpoints. Stops every agent running in the session.</summary>
    public abstract class StopChatSessionCommandBase : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }
    }

    /// <summary>US 7501: Stop from the authenticated tenant chat - <c>tenant/agent/{id}/chat/stop</c>.</summary>
    public class StopTenantChatSessionCommand : StopChatSessionCommandBase, IRequestId<int>
    {
        /// <summary>Tenant agent id, set from the route.</summary>
        [JsonIgnore]
        public int Id { get; set; }
    }

    /// <summary>US 7501: Stop from a shared/public chat - <c>agent/{shareId}/chat/stop</c>.</summary>
    public class StopPublicChatSessionCommand : StopChatSessionCommandBase, IRequestShareId<string>
    {
        /// <summary>Set from the route.</summary>
        [JsonIgnore]
        public string ShareId { get; set; }
    }

    /// <summary>Response of a Stop.</summary>
    public class StopChatSessionResult
    {
        public string SessionId { get; set; }

        /// <summary>UTC instant of the Stop. The same value is sent in the <c>ConversationStopped</c> hub event.</summary>
        public DateTimeOffset StoppedAt { get; set; }
    }
}
