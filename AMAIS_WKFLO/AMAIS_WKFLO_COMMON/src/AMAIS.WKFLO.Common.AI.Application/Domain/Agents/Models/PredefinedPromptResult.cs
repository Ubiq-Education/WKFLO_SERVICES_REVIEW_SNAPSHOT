using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models
{
    public class PredefinedPromptResult : IMap
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Prompt { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<PredefinedPrompt, PredefinedPromptResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<PredefinedPrompt, PredefinedPromptResult>();
        }
    }
}

