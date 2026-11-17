using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitContactForm
{
    public class SubmitContactFormCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [JsonIgnore]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SubjectId { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string OriginalMessage { get; set; }
    }
}
