using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentConversations
{
    /// <summary>
    /// US 7507: the caller's MAISY conversations for the agent behind <see cref="ShareId"/>.
    /// </summary>
    public class ListPublicAgentConversationsQuery : IWkfloRequest<Result>, IRequestShareId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }
    }
}
