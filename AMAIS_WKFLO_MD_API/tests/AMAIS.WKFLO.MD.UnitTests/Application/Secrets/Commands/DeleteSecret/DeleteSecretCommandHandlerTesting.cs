using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.DeleteSecret
{
    public class DeleteSecretCommandHandlerTesting
    {
        public DeleteSecretCommandHandler DeleteSecretCommandHandler { get; set; }
        public int SecretId { get; set; } = 1;

        public DeleteSecretCommandHandlerTesting()
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

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Secrets).Returns(secrets.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            DeleteSecretCommandHandler = new DeleteSecretCommandHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
