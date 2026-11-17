using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession
{
    public class LoadRunnableAgentChatHistoryBySessionQuery : IWkfloRequest<(DateTimeOffset, string, ChatHistory)>
    {
        [JsonIgnore]
        public RunnableAgent Agent { get; set; }

        [Required(ErrorMessage = CommonErrorConst.Required)]
        public string SessionId { get; set; }
    }
}

