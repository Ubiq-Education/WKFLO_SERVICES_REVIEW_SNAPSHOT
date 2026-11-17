using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Knowledge.Queries.ListTenantKnowledge;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Queries.ListTenantKnowledge
{
    public class ListTenantKnowledgeQueryHandlerTesting
    {
        public const string TenantName = "Test";
        public static readonly DateTimeOffset FixedTimestamp = new(2026, 2, 3, 8, 30, 0, TimeSpan.Zero);

        public ListTenantKnowledgeQueryHandler Handler { get; }
        public Mock<IDatastreamProvider> DatastreamProviderMock { get; }

        public ListTenantKnowledgeQueryHandlerTesting()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };

            var elasticsearchSettings = Options.Create(new ElasticsearchSettings
            {
                KnowledgeIndex = "tenant-knowledge-index"
            });

            var mapper = WkfloMapperExtensions.CreateMapper();

            DatastreamProviderMock = new Mock<IDatastreamProvider>();
            DatastreamProviderMock
                .Setup(x => x.ListAllKnowledgeAsync<BaseKnowledgeResult>(TenantName, "tenant-knowledge-index"))
                .ReturnsAsync(new List<BaseKnowledgeResult>
                {
                    new()
                    {
                        Id = "doc-1",
                        FileName = "admissions-faq.pdf",
                        ContentType = "application/pdf",
                        TenantName = TenantName,
                        Tags = new List<string> { "admissions" },
                        BlobUrl = "https://storage.example.com/tenant-knowledge/admissions-faq.pdf",
                        Description = "Full admissions process and requirements",
                        Timestamp = FixedTimestamp
                    },
                    new()
                    {
                        Id = "doc-2",
                        FileName = "no-description.docx",
                        ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        TenantName = TenantName,
                        Tags = new List<string>(),
                        BlobUrl = "https://storage.example.com/tenant-knowledge/no-description.docx",
                        Description = null,
                        Timestamp = FixedTimestamp
                    }
                });

            Handler = new ListTenantKnowledgeQueryHandler(
                DatastreamProviderMock.Object,
                elasticsearchSettings,
                mapper,
                tenantContext);
        }
    }
}
