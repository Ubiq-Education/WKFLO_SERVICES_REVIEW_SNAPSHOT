using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.AIModels.Models
{
    public class AIModelDeleteResult : IMapFrom<AIModel>
    {
        public int Id { get; set; }
    }
}
