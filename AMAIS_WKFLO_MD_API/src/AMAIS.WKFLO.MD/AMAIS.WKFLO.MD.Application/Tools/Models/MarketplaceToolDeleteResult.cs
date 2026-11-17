using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class MarketplaceToolDeleteResult : IMapFrom<Tool>
    {
        public int Id { get; set; }
    }
}
