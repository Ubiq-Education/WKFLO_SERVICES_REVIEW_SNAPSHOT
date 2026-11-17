using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins
{
    public class SyncTenantWebOriginsCommand : IWkfloRequest<Result>
    {
        public List<SyncTenantWebOriginItem> WebOrigins { get; set; }
    }

    public class SyncTenantWebOriginItem
    {
        public string WebOriginType { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }
}

