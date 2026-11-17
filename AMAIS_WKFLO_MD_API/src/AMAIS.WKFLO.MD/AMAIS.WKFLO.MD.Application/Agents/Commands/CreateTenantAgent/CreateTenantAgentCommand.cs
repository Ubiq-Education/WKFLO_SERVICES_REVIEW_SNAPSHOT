using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent
{
    public class CreateTenantAgentCommand : IWkfloRequest<Result>, IMap
    {
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

        public bool IsActive { get; set; }

        public bool EnableSTM { get; set; }

        public bool EnableLTM { get; set; }

        public bool EnableSpinUpAgents { get; set; }

        public int? AIModelId { get; set; }

        public int? MaxToolCallsPerTurn { get; set; } = 3;

        /// <summary>Optional admin-supplied instructions appended after the SystemPrompt at runtime (WI 7318).</summary>
        [MaxLength(RunnableAgent.AdditionalInstructionsMaxLength, ErrorMessage = ErrorConst.MaximumLength)]
        public string AdditionalInstructions { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<CreateTenantAgentCommand, TenantAgent>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateTenantAgentCommand, TenantAgent>();
        }
    }
}

