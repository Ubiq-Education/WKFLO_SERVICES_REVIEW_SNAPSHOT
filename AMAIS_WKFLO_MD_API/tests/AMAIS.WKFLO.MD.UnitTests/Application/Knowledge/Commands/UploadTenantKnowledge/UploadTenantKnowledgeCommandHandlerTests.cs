using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UploadTenantKnowledge;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.UploadTenantKnowledge
{
    public class UploadTenantKnowledgeCommandHandlerTests : IClassFixture<UploadTenantKnowledgeCommandHandlerTesting>
    {
        private readonly UploadTenantKnowledgeCommandHandlerTesting _testing;

        public UploadTenantKnowledgeCommandHandlerTests(UploadTenantKnowledgeCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task Handle_WithDescription_PersistsBothTitleAndDescriptionOnKnowledgeDocument()
        {
            // Arrange — Title is derived from the filename by the uploader endpoint (not removed
            // or repurposed by the Description addition — both fields are populated independently
            // on every upload) and passed through on the command, same as Description.
            var command = new UploadTenantKnowledgeCommand
            {
                FileName = "admissions-faq.pdf",
                Title = "admissions-faq",
                Description = "Full admissions process and requirements",
                ContentType = "application/pdf",
                Size = 1024,
                Base64Content = "ZmFrZS1jb250ZW50",
                Tags = new List<string> { "admissions" },
                BlobUrl = "https://storage.example.com/tenant-knowledge/admissions-faq.pdf"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<string>>(result);
            Assert.NotEmpty(_testing.IndexedDocuments);
            var indexed = _testing.IndexedDocuments[^1];
            Assert.Equal("admissions-faq", indexed.Title);
            Assert.Equal("Full admissions process and requirements", indexed.Description);
            Assert.Equal(command.FileName, indexed.FileName);
            Assert.Equal(command.BlobUrl, indexed.BlobUrl);
        }

        [Fact]
        public async Task Handle_WithoutDescription_StillPersistsFilenameDerivedTitleAndNullDescription()
        {
            // Arrange — description is optional, matching the multipart endpoint not sending it,
            // but Title is always derived from the filename regardless of whether a description
            // was provided.
            var command = new UploadTenantKnowledgeCommand
            {
                FileName = "no-description.docx",
                Title = "no-description",
                Description = null,
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Size = 2048,
                Base64Content = "ZmFrZS1jb250ZW50LTI=",
                Tags = new List<string>(),
                BlobUrl = "https://storage.example.com/tenant-knowledge/no-description.docx"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<string>>(result);
            Assert.NotEmpty(_testing.IndexedDocuments);
            var indexed = _testing.IndexedDocuments[^1];
            Assert.Equal("no-description", indexed.Title);
            Assert.Null(indexed.Description);
        }
    }
}
