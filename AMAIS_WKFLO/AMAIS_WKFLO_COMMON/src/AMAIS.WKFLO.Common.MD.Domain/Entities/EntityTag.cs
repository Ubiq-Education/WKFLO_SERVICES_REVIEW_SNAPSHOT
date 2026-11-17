namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class EntityTag
    {
        public int TagId { get; set; }
        public string EntityType { get; set; }
        public int EntityId { get; set; }
        public Tag Tag { get; set; }
    }
}
