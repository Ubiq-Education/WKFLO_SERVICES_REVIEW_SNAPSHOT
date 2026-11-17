using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class UserInstall : Entity<int>
    {
        public string Email { get; set; }
        public string Type { get; set; }
        public int TypeId { get; set; }

        public Guid? UserId { get; set; }
        public User User { get; set; }
    }
}