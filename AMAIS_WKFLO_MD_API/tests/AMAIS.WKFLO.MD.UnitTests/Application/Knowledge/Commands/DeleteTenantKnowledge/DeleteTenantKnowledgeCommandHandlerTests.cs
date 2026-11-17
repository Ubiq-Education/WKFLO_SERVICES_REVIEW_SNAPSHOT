using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge;
using AMAIS.WKFLO.MD.Application.Knowledge.Models;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Knowledge.Commands.DeleteTenantKnowledge
{
    public class DeleteTenantKnowledgeCommandHandlerTests : IClassFixture<DeleteTenantKnowledgeCommandHandlerTesting>, IDisposable
    {
        private const string Tenant = DeleteTenantKnowledgeCommandHandlerTesting.TenantName;
        private const string Container = DeleteTenantKnowledgeCommandHandlerTesting.Container;
        private const string BlobUrlBase = DeleteTenantKnowledgeCommandHandlerTesting.BlobUrlBase;

        private readonly DeleteTenantKnowledgeCommandHandlerTesting _testing;

        public DeleteTenantKnowledgeCommandHandlerTests(DeleteTenantKnowledgeCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        // The fixture's mocks are shared across tests; clear recorded calls so each Verify only sees its own test.
        public void Dispose()
        {
            _testing.DatastreamProviderMock.Invocations.Clear();
            _testing.BlobStorageProviderMock.Invocations.Clear();
        }

        [Fact]
        public async Task Handle_ExistingDocument_DeletesIndexEntryAndUploadedFile()
        {
            _testing.SetupDocument("doc-1", $"{BlobUrlBase}{Tenant}/1f2e_guide.pdf");

            var result = await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-1" }, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantKnowledgeDeleteResult>>(result);
            Assert.Equal("doc-1", success.Data.Id);
            _testing.DatastreamProviderMock.Verify(x => x.DeleteDocumentAsync("doc-1", DeleteTenantKnowledgeCommandHandlerTesting.KnowledgeIndex), Times.Once);
            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(Container, $"{Tenant}/1f2e_guide.pdf", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_EncodedFileName_DeletesBlobWithDecodedName()
        {
            _testing.SetupDocument("doc-2", $"{BlobUrlBase}{Tenant}/9a8b_my%20guide.pdf");

            await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-2" }, TestContext.Current.CancellationToken);

            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(Container, $"{Tenant}/9a8b_my guide.pdf", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_UrlPointsToAnotherTenantFolder_DeletesOnlyInCallersTenantFolder()
        {
            // The blob name is rebuilt under the caller's tenant, so a stored URL can never reach another tenant's file.
            _testing.SetupDocument("doc-3", $"{BlobUrlBase}OtherTenant/5c6d_report.pdf");

            await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-3" }, TestContext.Current.CancellationToken);

            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(Container, $"{Tenant}/5c6d_report.pdf", It.IsAny<CancellationToken>()), Times.Once);
            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(Container, It.Is<string>(n => n.StartsWith("OtherTenant/")), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_IndexDeleteFails_ReturnsNotFoundAndKeepsFile()
        {
            _testing.SetupDocument("doc-4", $"{BlobUrlBase}{Tenant}/7e8f_kept.pdf", deleted: false);

            var result = await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-4" }, TestContext.Current.CancellationToken);

            Assert.IsType<NotFoundResult>(result);
            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_DocumentHasNoFileUrl_DeletesIndexEntryWithoutTouchingStorage()
        {
            _testing.SetupDocument("doc-5", blobUrl: null);

            var result = await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-5" }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantKnowledgeDeleteResult>>(result);
            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_FileDeleteThrows_StillReturnsSuccess()
        {
            // The index entry is already gone; a storage failure is logged and must not fail the request.
            const string blobName = $"{Tenant}/3a4b_flaky.pdf";
            _testing.SetupDocument("doc-6", $"{BlobUrlBase}{blobName}");
            _testing.BlobStorageProviderMock
                .Setup(x => x.DeleteAsync(Container, blobName, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage unavailable"));

            var result = await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-6" }, TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantKnowledgeDeleteResult>>(result);
        }

        [Fact]
        public async Task Handle_DocumentReadFails_ReturnsErrorAndDeletesNothing()
        {
            // A failed read must not delete the index entry, or the file's URL would be lost and the file orphaned.
            _testing.SetupDocument("doc-7", $"{BlobUrlBase}{Tenant}/2b3c_unread.pdf", readable: false);

            var result = await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-7" }, TestContext.Current.CancellationToken);

            Assert.IsType<UnexpectedResult>(result);
            _testing.DatastreamProviderMock.Verify(x => x.DeleteDocumentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_RequestCancelled_StillDeletesFileWithoutCancellation()
        {
            // The index entry is already deleted, so the file cleanup must not be cancelled with the request.
            _testing.SetupDocument("doc-8", $"{BlobUrlBase}{Tenant}/4d5e_cancelled.pdf");
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await _testing.Handler.Handle(new DeleteTenantKnowledgeCommand { Id = "doc-8" }, cts.Token);

            _testing.BlobStorageProviderMock.Verify(x => x.DeleteAsync(Container, $"{Tenant}/4d5e_cancelled.pdf", CancellationToken.None), Times.Once);
        }
    }
}
