using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel
{
    public class UpdateAIModelCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

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

        public bool? IsActive { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateAIModelCommand, AIModel>();
        //             profile.CreateMap<AIModel, UpdateAIModelCommand>();
        //             profile.CreateMap<UpdateAIModelCommand, UpdateAIModelCommand>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateAIModelCommand, AIModel>();
            config.NewConfig<AIModel, UpdateAIModelCommand>();
            config.NewConfig<UpdateAIModelCommand, UpdateAIModelCommand>();
        }
    }
}

