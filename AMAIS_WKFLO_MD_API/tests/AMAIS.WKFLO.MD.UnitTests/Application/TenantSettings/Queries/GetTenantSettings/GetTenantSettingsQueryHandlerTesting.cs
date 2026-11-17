using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.TenantSettings.Queries.GetTenantSettings;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Queries.GetTenantSettings
{
    public class GetTenantSettingsQueryHandlerTesting
    {
        public const string TenantName = "test-tenant";
        public const int TenantId = 1;
        public const int OtherTenantId = 2;

        /// <summary>
        /// Builds a handler over the given settings rows. The tenant list always contains
        /// <see cref="TenantName"/> (Id <see cref="TenantId"/>) and another tenant (Id <see cref="OtherTenantId"/>).
        /// </summary>
        public GetTenantSettingsQueryHandler CreateHandler(List<TenantSetting> settings, string requestTenantName = TenantName)
        {
            var tenants = new List<Tenant>
            {
                new() { Id = TenantId, Name = TenantName },
                new() { Id = OtherTenantId, Name = "other-tenant" }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.TenantSettings).Returns(settings.BuildMockDbSet().Object);

            return new GetTenantSettingsQueryHandler(db.Object, new TenantContext { TenantName = requestTenantName });
        }
    }
}
