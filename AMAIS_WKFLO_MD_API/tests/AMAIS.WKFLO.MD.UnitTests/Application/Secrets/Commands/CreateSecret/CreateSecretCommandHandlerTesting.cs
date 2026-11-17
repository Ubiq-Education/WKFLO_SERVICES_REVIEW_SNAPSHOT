using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.CreateSecret
{
    public class CreateSecretCommandHandlerTesting
    {
        public CreateSecretCommandHandler CreateSecretCommandHandler { get; set; }

        public CreateSecretCommandHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var secrets = new List<Secret>() { }.BuildMockDbSet();
            var tenants = new List<Tenant>() { }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Secrets).Returns(secrets.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            CreateSecretCommandHandler = new CreateSecretCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
