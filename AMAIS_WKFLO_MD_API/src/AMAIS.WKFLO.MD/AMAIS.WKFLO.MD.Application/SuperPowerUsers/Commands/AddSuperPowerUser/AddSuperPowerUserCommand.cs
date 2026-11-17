using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.SuperPowerUsers.Commands.AddSuperPowerUser
{
    public class AddSuperPowerUserCommand : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(320, ErrorMessage = ErrorConst.MaximumLength)]
        public string Email { get; set; }
    }
}
