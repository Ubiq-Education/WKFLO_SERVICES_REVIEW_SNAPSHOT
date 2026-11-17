using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.AICredits.Models
{
    public class AICreditUpdateResult : IMap
    {
        public int Id { get; set; }
        public int AIModelId { get; set; }
        public long TokensIn { get; set; }
        public decimal CostInMTok { get; set; }
        public long TokensOut { get; set; }
        public decimal CostOutMTok { get; set; }
        public decimal TotalCost { get; set; }
        public decimal MultiplierIn { get; set; }
        public decimal MultiplierOut { get; set; }
        public decimal Credits { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AICredit, AICreditUpdateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AICredit, AICreditUpdateResult>();
        }
    }
}
