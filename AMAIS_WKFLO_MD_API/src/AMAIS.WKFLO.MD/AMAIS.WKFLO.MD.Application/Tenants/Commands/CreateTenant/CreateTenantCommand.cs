using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant
{
    public class CreateTenantCommand : IWkfloRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string Region { get; set; }

        public List<CreateTenantWebOriginItem> WebOrigins { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateTenantCommand, Tenant>();
        }
    }

    public class CreateTenantWebOriginItem
    {
        public string WebOriginType { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }
}

