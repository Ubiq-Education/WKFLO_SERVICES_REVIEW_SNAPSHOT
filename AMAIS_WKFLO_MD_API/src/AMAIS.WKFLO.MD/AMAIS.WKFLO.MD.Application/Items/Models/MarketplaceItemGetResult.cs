namespace AMAIS.WKFLO.MD.Application.Items.Models
{
    public class MarketplaceItemGetResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal Stars { get; set; }
        public decimal Installations { get; set; }
        public bool UserClickStar { get; set; }
        public bool UserClickInstall { get; set; }
        public string Price { get; set; }
        public int Downloads { get; set; }
        public string Vendor { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
    }
}


