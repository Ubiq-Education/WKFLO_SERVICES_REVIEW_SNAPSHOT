using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent
{
    public class UpdateTenantAgentCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
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

        [MaxLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string ResponseTone { get; set; }

        public bool? IsActive { get; set; }

        public bool? EnableSTM { get; set; }

        public bool? EnableLTM { get; set; }

        public bool? EnableSpinUpAgents { get; set; }

        public int? AIModelId { get; set; }

        public int? MaxToolCallsPerTurn { get; set; }

        /// <summary>
        /// Admin-supplied instructions appended after the platform SystemPrompt (WI 7318).
        /// <c>null</c> = leave the stored value unchanged (so callers that don't know this field can't
        /// wipe it); empty/whitespace = clear it; anything else replaces it.
        /// </summary>
        [MaxLength(RunnableAgent.AdditionalInstructionsMaxLength, ErrorMessage = ErrorConst.MaximumLength)]
        public string AdditionalInstructions { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateTenantAgentCommand, TenantAgent>();
        //             profile.CreateMap<TenantAgent, UpdateTenantAgentCommand>();
        //             profile.CreateMap<UpdateTenantAgentCommand, UpdateTenantAgentCommand>()
        //                 .ForAllMembers(opts =>
        //                 {
        //                     opts.Condition((src, dest, srcMember) => srcMember != null);
        //                 });
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            // AdditionalInstructions has null = unchanged / "" = clear semantics, which a plain
            // convention copy can't express — UpdateTenantAgentCommandHandler applies it explicitly.
            config.NewConfig<UpdateTenantAgentCommand, TenantAgent>()
                .Ignore(dest => dest.AdditionalInstructions);
            config.NewConfig<TenantAgent, UpdateTenantAgentCommand>();
            config.NewConfig<UpdateTenantAgentCommand, UpdateTenantAgentCommand>()
                .IgnoreNullValues(true);
        }
    }
}

