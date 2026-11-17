using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RunPublicAgentByShareId
{
    public class RunPublicAgentByShareIdCommand : IWkfloRequest<Result>, IRequestShareId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public RunPublicAgentInputCommand Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }
    }

    public class RunPublicAgentInputCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public List<FileAttachment> Files { get; set; }
    }
}
