using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret
{
    public class UpdateSecretCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string ApiKey { get; set; }

        public bool? IsActive { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateSecretCommand, Secret>();
        //             profile.CreateMap<Secret, UpdateSecretCommand>();
        //             profile.CreateMap<UpdateSecretCommand, UpdateSecretCommand>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateSecretCommand, Secret>();
            config.NewConfig<Secret, UpdateSecretCommand>();
            config.NewConfig<UpdateSecretCommand, UpdateSecretCommand>();
        }
    }
}

