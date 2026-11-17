using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool
{
    public class UpdateMarketplaceToolCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

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
        //             profile.CreateMap<UpdateMarketplaceToolCommand, MarketplaceTool>();
        //             profile.CreateMap<MarketplaceTool, UpdateMarketplaceToolCommand>();
        //             profile.CreateMap<UpdateMarketplaceToolCommand, UpdateMarketplaceToolCommand>()
        //                 .ForAllMembers(opts =>
        //                 {
        //                     opts.Condition((src, dest, srcMember) => srcMember != null);
        //                 });
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateMarketplaceToolCommand, MarketplaceTool>();
            config.NewConfig<MarketplaceTool, UpdateMarketplaceToolCommand>();
            config.NewConfig<UpdateMarketplaceToolCommand, UpdateMarketplaceToolCommand>()
                .IgnoreNullValues(true);
        }
    }
}

