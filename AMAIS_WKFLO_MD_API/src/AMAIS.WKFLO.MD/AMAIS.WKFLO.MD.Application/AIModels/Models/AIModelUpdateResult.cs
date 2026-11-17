using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.AIModels.Models
{
    public class AIModelUpdateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Model { get; set; }
        public string DeploymentName { get; set; }
        public string Provider { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }
        public string Type { get; set; }
        public string AuthMode { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AIModel, AIModelUpdateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AIModel, AIModelUpdateResult>();
        }
    }
}


