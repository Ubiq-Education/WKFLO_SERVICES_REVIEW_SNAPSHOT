using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret
{
    public class CreateSecretCommand : IWkfloRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string ApiKey { get; set; }

        public bool IsActive { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateSecretCommand, Secret>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateSecretCommand, Secret>();
        }
    }
}

