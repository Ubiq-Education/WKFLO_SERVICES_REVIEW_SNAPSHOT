using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.DeleteTenantKnowledge
{
    public class DeleteTenantKnowledgeCommandHandlerTesting
    {
        public const string TenantName = "Test";
        public const string KnowledgeIndex = "tenant-knowledge-index";
        public const string Container = DeleteTenantKnowledgeCommandHandler.KnowledgeContainerName;
        public const string BlobUrlBase = "https://storage.example.com/tenant-knowledge/";

        public DeleteTenantKnowledgeCommandHandler Handler { get; }
        public Mock<IDatastreamProvider> DatastreamProviderMock { get; }
        public Mock<IBlobStorageProvider> BlobStorageProviderMock { get; }

        public DeleteTenantKnowledgeCommandHandlerTesting()
        {
            DatastreamProviderMock = new Mock<IDatastreamProvider>();
            BlobStorageProviderMock = new Mock<IBlobStorageProvider>();

            DatastreamProviderMock
                .Setup(x => x.RefreshIndexAsync(KnowledgeIndex))
                .ReturnsAsync(true);
            BlobStorageProviderMock
                .Setup(x => x.DeleteAsync(Container, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Handler = new DeleteTenantKnowledgeCommandHandler(
                Mock.Of<ILogger<DeleteTenantKnowledgeCommandHandler>>(),
                DatastreamProviderMock.Object,
                BlobStorageProviderMock.Object,
                Options.Create(new ElasticsearchSettings { KnowledgeIndex = KnowledgeIndex }),
                new TenantContext { TenantName = TenantName });
        }

        /// <summary>A document that exists in the index and deletes successfully.</summary>
        /// <param name="readable">False makes the read return null, as the provider does on an Elasticsearch error.</param>
        public void SetupDocument(string docId, string blobUrl, bool deleted = true, bool readable = true)
        {
            DatastreamProviderMock
                .Setup(x => x.GetDocumentByIdAsync<BaseKnowledgeResult>(docId, TenantName, KnowledgeIndex))
                .ReturnsAsync(readable ? new BaseKnowledgeResult { Id = docId, BlobUrl = blobUrl } : null);
            DatastreamProviderMock
                .Setup(x => x.DeleteDocumentAsync(docId, KnowledgeIndex))
                .ReturnsAsync(deleted);
        }
    }
}
