using AMAIS.WKFLO.Common.Models.Entities.Interfaces;

namespace AMAIS.WKFLO.Common.Models.Entities
{
    public abstract class BaseTimeEntity<T> : BaseEntity<T>, IEntity
    {
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        public string GetEntityId()
        {
            return Id.ToString();
        }
    }
}
