using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Commands.UpdateTenantDxpIntegration
{
    public class UpdateTenantDxpIntegrationCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [JsonIgnore]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string DxpTenantName { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(1000, ErrorMessage = ErrorConst.MaximumLength)]
        public string Domain { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string ApiKey { get; set; }

        public bool IsActive { get; set; }
    }
}
