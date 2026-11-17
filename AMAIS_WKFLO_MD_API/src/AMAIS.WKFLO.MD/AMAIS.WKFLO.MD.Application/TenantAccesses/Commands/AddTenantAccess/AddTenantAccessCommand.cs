using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Commands.AddTenantAccess
{
    public class AddTenantAccessCommand : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public Guid? TenantGuid { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(320, ErrorMessage = ErrorConst.MaximumLength)]
        public string Email { get; set; }
    }
}
