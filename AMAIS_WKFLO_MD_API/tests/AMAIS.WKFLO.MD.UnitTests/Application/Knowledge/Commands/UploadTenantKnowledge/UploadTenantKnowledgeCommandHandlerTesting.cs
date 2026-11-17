using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UploadTenantKnowledge;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.UploadTenantKnowledge
{
    public class UploadTenantKnowledgeCommandHandlerTesting
    {
        public const string TenantName = "Test";
        public static readonly DateTimeOffset FixedDate = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public UploadTenantKnowledgeCommandHandler Handler { get; }
        public Mock<IDatastreamProvider> DatastreamProviderMock { get; }
        public List<KnowledgeDocument> IndexedDocuments { get; } = new();

        public UploadTenantKnowledgeCommandHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };

            var elasticsearchSettings = Options.Create(new ElasticsearchSettings
            {
                KnowledgeIndex = "tenant-knowledge-index"
            });

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FixedDate);

            DatastreamProviderMock = new Mock<IDatastreamProvider>();
            DatastreamProviderMock
                .Setup(x => x.PutAllKnowledgePipelineAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            DatastreamProviderMock
                .Setup(x => x.RefreshIndexAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            DatastreamProviderMock
                .Setup(x => x.IndexAsync(It.IsAny<KnowledgeDocument>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<KnowledgeDocument, string, string>((doc, _, _) => IndexedDocuments.Add(doc))
                .Returns(Task.CompletedTask);

            Handler = new UploadTenantKnowledgeCommandHandler(
                DatastreamProviderMock.Object,
                elasticsearchSettings,
                tenantContext,
                dateTimeProviderMock.Object);
        }
    }
}
