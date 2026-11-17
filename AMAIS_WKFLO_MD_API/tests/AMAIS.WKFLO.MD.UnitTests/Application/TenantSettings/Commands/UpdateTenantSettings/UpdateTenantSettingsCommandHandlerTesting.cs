using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantSettings.Commands.UpdateTenantSettings
{
    public class UpdateTenantSettingsCommandHandlerTesting
    {
        public const string TenantName = "test-tenant";
        public const int TenantId = 1;

        /// <summary>One handler instance plus the mocks/state a test needs to assert against.</summary>
        public sealed class Scenario
        {
            public UpdateTenantSettingsCommandHandler Handler { get; init; }
            public Mock<IMasterDataDbContext> Db { get; init; }
            public List<TenantSetting> Added { get; } = new();
        }

        /// <summary>
        /// Builds a fresh handler per test (the command mutates the seeded rows, so state must not be
        /// shared across tests through the class fixture).
        /// </summary>
        public Scenario CreateScenario(List<TenantSetting> existingSettings, string requestTenantName = TenantName)
        {
            var tenants = new List<Tenant>
            {
                new() { Id = TenantId, Name = TenantName }
            }.BuildMockDbSet();

            var settingsDbSet = existingSettings.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.TenantSettings).Returns(settingsDbSet.Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var scenario = new Scenario
            {
                Db = db,
                Handler = new UpdateTenantSettingsCommandHandler(db.Object, new TenantContext { TenantName = requestTenantName })
            };

            settingsDbSet
                .Setup(x => x.Add(It.IsAny<TenantSetting>()))
                .Callback<TenantSetting>(scenario.Added.Add);

            return scenario;
        }
    }
}
