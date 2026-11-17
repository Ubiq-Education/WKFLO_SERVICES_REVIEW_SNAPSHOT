using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool
{
    public class CreateTenantToolCommand : IWkfloRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(300, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Description { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Type { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Config { get; set; }

        public bool IsActive { get; set; }

        /// <summary>Max output chars before SemanticKernelLargeToolOutputFilter truncates this tool's
        /// results. Null (the default) means no cap.</summary>
        public int? MaxToolOutputLength { get; set; }

        /// <summary>
        /// WI 7575: optional. Tool permissions are opt-in: null (the default) means false.
        /// </summary>
        public bool? PermissionsEnabled { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateTenantToolCommand, TenantTool>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateTenantToolCommand, TenantTool>()
                .Ignore(dest => dest.PermissionsEnabled);
        }
    }
}

