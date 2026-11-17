using AMAIS.WKFLO.Common.Models.Entities.Interfaces;

namespace AMAIS.WKFLO.Common.Models.Entities
{
    public abstract class Entity<T> : BaseTimeEntity<T>, IEntity
    {
        public bool IsDeleted { get; set; }
    }
}
