using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommand : IWkfloRequest<Result>, IMap
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

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateMarketplaceToolCommand, MarketplaceTool>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateMarketplaceToolCommand, MarketplaceTool>();
        }
    }
}

