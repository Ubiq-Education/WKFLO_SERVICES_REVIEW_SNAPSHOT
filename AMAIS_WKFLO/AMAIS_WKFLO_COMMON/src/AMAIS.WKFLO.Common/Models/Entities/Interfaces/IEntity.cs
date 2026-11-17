namespace AMAIS.WKFLO.Common.Models.Entities.Interfaces
{
    public interface IEntity
    {
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
        public string GetEntityId();
    }
}
