using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Secrets.Models
{
    public class SecretCreateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<Secret, SecretCreateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<Secret, SecretCreateResult>();
        }
    }
}


