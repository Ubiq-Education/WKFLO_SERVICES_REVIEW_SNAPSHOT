using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Models
{
    public class TenantKnowledgeSearchResult : IMap
    {
        public double Similarity { get; set; }
        public string Source { get; set; }
        public string Text { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<SearchKnowledgeResult, TenantKnowledgeSearchResult>()
        //                 .ForMember(dest => dest.Source, source => source.MapFrom(m => m.FileName));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<SearchKnowledgeResult, TenantKnowledgeSearchResult>()
                .Map(dest => dest.Source, src => src.FileName);
        }
    }
}
