using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentUpdateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseTone { get; set; }
        /// <summary>Admin-supplied instructions appended after the SystemPrompt at runtime (WI 7318). Null when not set.</summary>
        public string AdditionalInstructions { get; set; }
        public bool IsActive { get; set; }
        public bool EnableSTM { get; set; }

        public bool EnableLTM { get; set; }
        public bool EnableSpinUpAgents { get; set; }
        public int? MaxToolCallsPerTurn { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public IEnumerable<TenantAgentToolResult> AgentTools { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantAgent, TenantAgentUpdateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantAgent, TenantAgentUpdateResult>();
        }
    }
}


