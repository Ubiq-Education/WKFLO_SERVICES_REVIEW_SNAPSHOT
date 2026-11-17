using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Secrets.Models
{
    public class SecretDeleteResult : IMapFrom<Secret>
    {
        public int Id { get; set; }
    }
}
