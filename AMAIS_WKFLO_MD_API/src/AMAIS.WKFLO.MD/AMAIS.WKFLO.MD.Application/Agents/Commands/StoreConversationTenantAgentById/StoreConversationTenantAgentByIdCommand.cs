using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.StoreConversationTenantAgentById
{
    public class StoreConversationTenantAgentByIdCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        public string Name { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public string UserId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MinLength(1)]
        public List<StoreConversationMessageInput> Messages { get; set; }
    }

    public class StoreConversationMessageInput
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Role { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public string MessageId { get; set; }
    }
}

