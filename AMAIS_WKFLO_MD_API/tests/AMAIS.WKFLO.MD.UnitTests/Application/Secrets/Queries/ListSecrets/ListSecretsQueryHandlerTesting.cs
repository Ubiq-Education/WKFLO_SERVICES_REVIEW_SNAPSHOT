using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.ListSecrets;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.ListSecrets
{
    public class ListSecretsQueryHandlerTesting
    {
        public ListSecretsQueryHandler ListSecretsQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int SecretId { get; private set; } = RandomNumber.Next();

        public ListSecretsQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var secret = new Secret()
            {
                Id = SecretId,
                ApiKey = "ApiKey Test",
                Name = "name",
                IsActive = true
            };

            var secret2 = new Secret()
            {
                Id = RandomNumber.Next(),
                ApiKey = "ApiKey Test",
                Name = "name",
                IsActive = true
            };

            var secrets = new List<Secret>() {
                secret, secret2
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Secrets).Returns(secrets.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            ListSecretsQueryHandler = new ListSecretsQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
