using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentChatConfiguration
{
    public class GetTenantAgentChatSettingQuery : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int AgentId { get; set; }

        public bool IsWidget { get; set; }
    }
}
