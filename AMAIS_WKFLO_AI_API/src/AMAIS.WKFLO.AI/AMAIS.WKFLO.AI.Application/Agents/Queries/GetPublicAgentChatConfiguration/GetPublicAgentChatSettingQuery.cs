using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatConfiguration
{
    public class GetPublicAgentChatSettingQuery : IWkfloRequest<Result>, IRequestShareId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }
    }
}
