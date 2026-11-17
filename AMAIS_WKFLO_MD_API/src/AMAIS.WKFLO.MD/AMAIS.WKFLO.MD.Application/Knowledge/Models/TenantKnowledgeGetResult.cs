using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Models
{
    public class TenantKnowledgeGetResult : IMap
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public List<string> Tags { get; set; }
        public string BlobUrl { get; set; }
        public string Description { get; set; }
        public DateTimeOffset Timestamp { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<BaseKnowledgeResult, TenantKnowledgeGetResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<BaseKnowledgeResult, TenantKnowledgeGetResult>();
        }
    }
}
