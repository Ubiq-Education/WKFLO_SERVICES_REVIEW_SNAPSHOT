using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent
{
    public class UpdateMarketplaceAgentCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(300, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string Description { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SystemPrompt { get; set; }

        public int? AIModelId { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string IconUrl { get; set; }

        /// <summary>Whether a <see cref="Common.MD.Domain.Entities.TenantAgent"/> installed from this
        /// template should get Short Term Memory enabled by default (see
        /// <see cref="Common.MD.Domain.Entities.MarketplaceAgent.EnableSTM"/>'s doc comment). Always
        /// sent by the edit form (full-form save, not a partial patch), same as the other fields
        /// above.</summary>
        public bool EnableSTM { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateMarketplaceAgentCommand, MarketplaceAgent>();
        //             profile.CreateMap<MarketplaceAgent, UpdateMarketplaceAgentCommand>();
        //             profile.CreateMap<UpdateMarketplaceAgentCommand, UpdateMarketplaceAgentCommand>()
        //                 .ForAllMembers(opts =>
        //                 {
        //                     opts.Condition((src, dest, srcMember) => srcMember != null);
        //                 });
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateMarketplaceAgentCommand, MarketplaceAgent>();
            config.NewConfig<MarketplaceAgent, UpdateMarketplaceAgentCommand>();
            config.NewConfig<UpdateMarketplaceAgentCommand, UpdateMarketplaceAgentCommand>()
                .IgnoreNullValues(true);
        }
    }
}

