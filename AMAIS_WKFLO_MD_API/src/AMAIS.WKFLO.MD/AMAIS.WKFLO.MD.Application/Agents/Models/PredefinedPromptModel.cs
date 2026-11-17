using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class PredefinedPromptModel : IMap
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Prompt { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<PredefinedPrompt, PredefinedPromptModel>().ReverseMap();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<PredefinedPrompt, PredefinedPromptModel>().TwoWays();
        }
    }
}
