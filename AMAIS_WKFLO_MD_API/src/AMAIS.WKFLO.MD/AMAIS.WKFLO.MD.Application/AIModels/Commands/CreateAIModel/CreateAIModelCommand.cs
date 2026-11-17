using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommand : IWkfloRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(300, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Model { get; set; }

        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string DeploymentName { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Provider { get; set; }

        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string Endpoint { get; set; }

        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string ApiVersion { get; set; }

        [MaxLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string AuthMode { get; set; }

        public int? SecretId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Type { get; set; }

        public bool IsActive { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateAIModelCommand, AIModel>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateAIModelCommand, AIModel>();
        }
    }
}

