using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UploadAgentChatIcon;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UploadAgentChatIcon
{
    public class UploadAgentChatIconCommandHandlerTesting
    {
        public const string TenantName = "Test";
        public const int ExistingAgentId = 1;
        public const int OtherTenantAgentId = 2;
        public const int DeletedAgentId = 3;

        public UploadAgentChatIconCommandHandler Handler { get; }
        public Mock<IImageStorageService> ImageStorageServiceMock { get; }
        public List<byte[]> UploadedContents { get; } = new();
        public List<string> UploadedRelativePaths { get; } = new();
        public List<string> UploadedContentTypes { get; } = new();

        public UploadAgentChatIconCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var otherTenant = new Tenant { Id = 2, Name = "Other" };

            var tenantAgents = new List<TenantAgent>
            {
                new() { Id = ExistingAgentId, Name = "Agent", Tenant = tenant, IsActive = true },
                new() { Id = OtherTenantAgentId, Name = "Other Tenant Agent", Tenant = otherTenant, IsActive = true },
                new() { Id = DeletedAgentId, Name = "Deleted Agent", Tenant = tenant, IsActive = true, IsDeleted = true }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);

            ImageStorageServiceMock = new Mock<IImageStorageService>();
            ImageStorageServiceMock
                .Setup(x => x.SaveImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string containerName, string relativePath, Stream content, string contentType, CancellationToken ct) =>
                {
                    using var buffer = new MemoryStream();
                    content.CopyTo(buffer);
                    UploadedContents.Add(buffer.ToArray());
                    UploadedRelativePaths.Add(relativePath);
                    UploadedContentTypes.Add(contentType);

                    return Task.FromResult(new ImageStorageResult
                    {
                        RelativePath = relativePath,
                        AbsoluteUrl = $"https://staging.static.wkflo.ai/{relativePath}"
                    });
                });

            Handler = new UploadAgentChatIconCommandHandler(
                db.Object,
                tenantContext,
                ImageStorageServiceMock.Object,
                new Mock<ILogger<UploadAgentChatIconCommandHandler>>().Object);
        }
    }
}
