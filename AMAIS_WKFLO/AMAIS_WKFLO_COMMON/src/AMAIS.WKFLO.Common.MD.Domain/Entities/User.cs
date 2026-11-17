using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class User : Entity<Guid>
    {
        public string Email { get; set; }
    }
}