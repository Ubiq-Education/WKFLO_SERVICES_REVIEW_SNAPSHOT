using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.UpdateSecret
{
    public class UpdateSecretCommandHandlerTesting
    {
        public UpdateSecretCommandHandler UpdateSecretCommandHandler { get; set; }
        public int SecretId { get; set; } = 1;

        public UpdateSecretCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var secret = new Secret()
            {
                Id = SecretId,
                ApiKey = "ApiKey Test",
                Name = "name",
                IsActive = true
            };

            var secrets = new List<Secret>() { secret }.BuildMockDbSet();
            var tenants = new List<Tenant>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Secrets).Returns(secrets.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            UpdateSecretCommandHandler = new UpdateSecretCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
