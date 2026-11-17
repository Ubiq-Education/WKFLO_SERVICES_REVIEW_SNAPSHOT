using AMAIS.WKFLO.Common.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UploadMarketplaceAgentIcon;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UploadMarketplaceAgentIcon
{
    public class UploadMarketplaceAgentIconCommandHandlerTesting
    {
        public const string TenantName = "Test";
        public const int ExistingAgentId = 1;
        // Shared by a DIFFERENT tenant than TenantName above — proves Marketplace-scope operations
        // are NOT tenant-filtered (WI 7312 fix): a marketplace agent shared by one tenant must
        // still be manageable regardless of which tenant happens to be in the caller's current
        // TenantContext, since this endpoint has no notion of "current tenant" at all anymore.
        public const int OtherTenantAgentId = 2;
        public const int DeletedAgentId = 3;

        public UploadMarketplaceAgentIconCommandHandler Handler { get; }
        public Mock<IImageStorageService> ImageStorageServiceMock { get; }
        public List<byte[]> UploadedContents { get; } = new();
        public List<string> UploadedRelativePaths { get; } = new();
        public List<string> UploadedContentTypes { get; } = new();

        public UploadMarketplaceAgentIconCommandHandlerTesting()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var otherTenant = new Tenant { Id = 2, Name = "Other" };

            var marketplaceAgents = new List<MarketplaceAgent>
            {
                new() { Id = ExistingAgentId, Name = "Agent", Tenant = tenant },
                new() { Id = OtherTenantAgentId, Name = "Other Tenant Agent", Tenant = otherTenant },
                new() { Id = DeletedAgentId, Name = "Deleted Agent", Tenant = tenant, IsDeleted = true }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceAgents).Returns(marketplaceAgents.Object);

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

            Handler = new UploadMarketplaceAgentIconCommandHandler(
                db.Object,
                ImageStorageServiceMock.Object,
                new Mock<ILogger<UploadMarketplaceAgentIconCommandHandler>>().Object);
        }
    }
}
