using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public int AIModelId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public long TokensIn { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public decimal CostInMTok { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public long TokensOut { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public decimal CostOutMTok { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public decimal MultiplierIn { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public decimal MultiplierOut { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateAICreditCommand, AICredit>();
        //             profile.CreateMap<AICredit, UpdateAICreditCommand>();
        //             profile.CreateMap<UpdateAICreditCommand, UpdateAICreditCommand>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateAICreditCommand, AICredit>();
            config.NewConfig<AICredit, UpdateAICreditCommand>();
            config.NewConfig<UpdateAICreditCommand, UpdateAICreditCommand>();
        }
    }
}
