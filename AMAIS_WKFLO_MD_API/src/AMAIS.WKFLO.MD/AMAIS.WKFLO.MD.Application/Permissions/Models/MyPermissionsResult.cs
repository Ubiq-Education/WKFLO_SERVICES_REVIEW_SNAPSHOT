namespace AMAIS.WKFLO.MD.Application.Permissions.Models
{
    public class MyPermissionsResult
    {
        public bool IsSuperPowerUser { get; set; }

        /// <summary>Guids of the tenants the caller may switch to. Empty for super power users (they see every tenant).</summary>
        public List<string> AccessibleTenantGuids { get; set; } = new();
    }
}
