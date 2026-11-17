using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.AICredits.Models
{
    public class AICreditDeleteResult : IMapFrom<AICredit>
    {
        public int Id { get; set; }
    }
}
