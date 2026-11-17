using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription;
using Moq;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription
{
    public class UpdateTenantKnowledgeDescriptionCommandHandlerTests : IClassFixture<UpdateTenantKnowledgeDescriptionCommandHandlerTesting>, IDisposable
    {
        private readonly UpdateTenantKnowledgeDescriptionCommandHandlerTesting _testing;

        public UpdateTenantKnowledgeDescriptionCommandHandlerTests(UpdateTenantKnowledgeDescriptionCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        // The fixture (and its mock) is shared across every test in this class via IClassFixture, but xUnit
        // creates a fresh instance of the test class itself — and calls Dispose on it — around each [Fact].
        // Clearing the mock's recorded invocations here (not its Setups, which stay intact and shared) after
        // every test means each test's Verify() only ever sees calls it made itself, regardless of what ran
        // before it or in what order — fixing the accumulation that made
        // Handle_ExistingDocument_UpdatesDescriptionAndReturnsSuccess's Times.Once assertion order-dependent.
        public void Dispose() => _testing.DatastreamProviderMock.Invocations.Clear();

        [Fact]
        public async Task Handle_ExistingDocument_UpdatesDescriptionAndReturnsSuccess()
        {
            // Arrange
            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = UpdateTenantKnowledgeDescriptionCommandHandlerTesting.ExistingDocId,
                Description = "Full admissions process and requirements"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            _testing.DatastreamProviderMock.Verify(
                x => x.UpdateDocumentDescriptionAsync(command.DocId, command.Description, UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ExistingDocument_RefreshesIndexAfterSuccessfulUpdate()
        {
            // Arrange — Elasticsearch's _search API is only near-real-time (default
            // refresh_interval ~1s), so a search immediately after the partial update can still
            // return the stale description unless the index is explicitly refreshed.
            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = UpdateTenantKnowledgeDescriptionCommandHandlerTesting.ExistingDocId,
                Description = "Full admissions process and requirements"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            _testing.DatastreamProviderMock.Verify(
                x => x.RefreshIndexAsync("tenant-knowledge-index"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_RefreshIndexFails_StillReturnsSuccessSinceDescriptionWasPersisted()
        {
            // Arrange — the description update itself succeeded; a refresh failure is a
            // best-effort visibility optimization and must not fail the overall operation.
            const string docId = "existing-but-refresh-fails";
            _testing.DatastreamProviderMock
                .Setup(x => x.ExistDocumentAsync<AMAIS.WKFLO.Common.Models.Results.Knowledge.KnowledgeDocument>(docId, UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"))
                .ReturnsAsync(true);
            _testing.DatastreamProviderMock
                .Setup(x => x.UpdateDocumentDescriptionAsync(docId, It.IsAny<string>(), UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"))
                .ReturnsAsync(true);
            _testing.DatastreamProviderMock
                .Setup(x => x.RefreshIndexAsync("tenant-knowledge-index"))
                .ThrowsAsync(new InvalidOperationException("Elasticsearch refresh failed"));

            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = docId,
                Description = "Some description"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
        }

        [Fact]
        public async Task Handle_ExistingDocument_AllowsClearingDescriptionWithNull()
        {
            // Arrange — description is optional/nullable, matching the tags endpoint's permissiveness.
            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = UpdateTenantKnowledgeDescriptionCommandHandlerTesting.ExistingDocId,
                Description = null
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            _testing.DatastreamProviderMock.Verify(
                x => x.UpdateDocumentDescriptionAsync(command.DocId, null, UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_DocumentDoesNotExistForTenant_ReturnsNotFound()
        {
            // Arrange
            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = UpdateTenantKnowledgeDescriptionCommandHandlerTesting.MissingDocId,
                Description = "Some description"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            _testing.DatastreamProviderMock.Verify(
                x => x.UpdateDocumentDescriptionAsync(command.DocId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_ProviderUpdateFails_ReturnsUnexpectedResult()
        {
            // Arrange
            const string docId = "existing-but-update-fails";
            _testing.DatastreamProviderMock
                .Setup(x => x.ExistDocumentAsync<AMAIS.WKFLO.Common.Models.Results.Knowledge.KnowledgeDocument>(docId, UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"))
                .ReturnsAsync(true);
            _testing.DatastreamProviderMock
                .Setup(x => x.UpdateDocumentDescriptionAsync(docId, It.IsAny<string>(), UpdateTenantKnowledgeDescriptionCommandHandlerTesting.TenantName, "tenant-knowledge-index"))
                .ReturnsAsync(false);

            var command = new UpdateTenantKnowledgeDescriptionCommand
            {
                DocId = docId,
                Description = "Some description"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<UnexpectedResult>(result);
        }
    }
}
