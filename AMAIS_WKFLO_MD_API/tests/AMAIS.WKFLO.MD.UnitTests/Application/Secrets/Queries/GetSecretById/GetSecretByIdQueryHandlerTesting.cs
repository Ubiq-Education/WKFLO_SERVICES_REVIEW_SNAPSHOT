using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.GetSecretById
{
    public class GetSecretByIdQueryHandlerTesting
    {
        public GetSecretByIdQueryHandler GetSecretByIdQueryHandler { get; set; }
        public static Random RandomNumber { get; private set; } = new Random();

        public int SecretId { get; private set; } = RandomNumber.Next();

        public GetSecretByIdQueryHandlerTesting()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var secret = new Secret()
            {
                Id = SecretId,
                ApiKey = "ApiKey Test",
                Name = "name",
                IsActive = true
            };

            var secrets = new List<Secret>() {
                secret
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Secrets).Returns(secrets.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            GetSecretByIdQueryHandler = new GetSecretByIdQueryHandler
            (
                db.Object,
                mapper
            );
        }
    }
}
