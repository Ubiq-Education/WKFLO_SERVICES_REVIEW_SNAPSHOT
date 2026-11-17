using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById
{
    public class VoiceMessageTenantAgentByIdCommand : IWkfloRequest<Result>, IWkfloQueueMessageCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int AgentId { get; set; }

        public bool ChainOfThought { get; set; }


        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public string BranchId { get; set; }

        public string GetId() => SessionId;
    }
}

