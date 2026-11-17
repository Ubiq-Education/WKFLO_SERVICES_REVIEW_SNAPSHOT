using AMAIS.DXP.INGRESS.Client.Lib.Api;
using AMAIS.DXP.INGRESS.Client.Lib.Models;
using AMAIS.DXP.MD.Client.Lib.Api;
using AMAIS.DXP.MD.Client.Lib.Models;
using AMAIS.WKFLO.Common.Infrastructure.Services.DXP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.DXP
{
    public class DxpContactFormServiceTests
    {
        private readonly Mock<IInteractionApi> _interactionApiMock;
        private readonly Mock<IContactApi> _contactApiMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly DxpContactFormService _service;

        private const string DxpTenantName = "dxp-tenant";
        private const string Domain = "https://dxp.example.com";
        private const string ApiKey = "api-key-123";
        private const string SubjectId = "subj-1";
        private const string SessionId = "sess-1";

        public DxpContactFormServiceTests()
        {
            _interactionApiMock = new Mock<IInteractionApi>();
            _interactionApiMock
                .Setup(x => x.InteractionWithIPCreateAsync(It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new BooleanSuccessResult());

            _contactApiMock = new Mock<IContactApi>();

            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            _httpContextAccessorMock.SetupGet(x => x.HttpContext).Returns((HttpContext)null);

            _service = new DxpContactFormService(
                _interactionApiMock.Object, _contactApiMock.Object, _httpContextAccessorMock.Object,
                Mock.Of<ILogger<DxpContactFormService>>());
        }

        [Fact]
        public async Task ReportContactFormSubmissionAsync_SendsSubjectAndSessionIdAsObject()
        {
            var result = await _service.ReportContactFormSubmissionAsync(
                DxpTenantName, Domain, ApiKey, SubjectId, SessionId, "John", "Doe", "john@example.com",
                tenantAgentId: 5, publishedTenantAgentId: 9, shareId: "share-abc");

            Assert.True(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.Predicate == "ChatBot Registration"
                    && cmd.Subject == SubjectId
                    && cmd.Object == SessionId
                    && cmd.ObjectMetadata.CorrelationId == null
                    && cmd.ObjectMetadata.SiteName == DxpTenantName
                    && !cmd.SubjectMetadata.ContainsKey("WebChatSessionId")),
                DxpTenantName, Domain, ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportContactFormSubmissionAsync_PopulatesObjectMetadataWithAgentIdentity()
        {
            // This is the fix for the always-zero leads-generated KPI: agent identity must be present in
            // ObjectMetadata.Metadata for DXP's per-agent filter to ever match a contact form submission.
            await _service.ReportContactFormSubmissionAsync(
                DxpTenantName, Domain, ApiKey, SubjectId, SessionId, "John", "Doe", "john@example.com",
                tenantAgentId: 5, publishedTenantAgentId: 9, shareId: "share-abc");

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.ObjectMetadata.Metadata["TenantAgentId"] == "5"
                    && cmd.ObjectMetadata.Metadata["PublishedTenantAgentId"] == "9"
                    && cmd.ObjectMetadata.Metadata["ShareId"] == "share-abc"),
                DxpTenantName, Domain, ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportContactFormSubmissionAsync_NullAgentIdentity_OmitsMetadataKeys()
        {
            await _service.ReportContactFormSubmissionAsync(
                DxpTenantName, Domain, ApiKey, SubjectId, SessionId, "John", "Doe", "john@example.com",
                tenantAgentId: null, publishedTenantAgentId: null, shareId: null);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    !cmd.ObjectMetadata.Metadata.ContainsKey("TenantAgentId")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("PublishedTenantAgentId")
                    && !cmd.ObjectMetadata.Metadata.ContainsKey("ShareId")),
                DxpTenantName, Domain, ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportContactFormSubmissionAsync_SendsContactDetailsInSubjectMetadata()
        {
            await _service.ReportContactFormSubmissionAsync(
                DxpTenantName, Domain, ApiKey, SubjectId, SessionId, "John", "Doe", "john@example.com",
                tenantAgentId: null, publishedTenantAgentId: null, shareId: null);

            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.Is<CreateInteractionWithIPCommand>(cmd =>
                    cmd.SubjectMetadata["FirstName"] == "John"
                    && cmd.SubjectMetadata["LastName"] == "Doe"
                    && cmd.SubjectMetadata["Email"] == "john@example.com"
                    && cmd.SubjectMetadata["ContactType"] == "Chat"),
                DxpTenantName, Domain, ApiKey), Times.Once);
        }

        [Fact]
        public async Task ReportContactFormSubmissionAsync_MissingSubjectId_DoesNotCallApiAndReturnsFalse()
        {
            var result = await _service.ReportContactFormSubmissionAsync(
                DxpTenantName, Domain, ApiKey, string.Empty, SessionId, "John", "Doe", "john@example.com",
                tenantAgentId: null, publishedTenantAgentId: null, shareId: null);

            Assert.False(result);
            _interactionApiMock.Verify(x => x.InteractionWithIPCreateAsync(
                It.IsAny<CreateInteractionWithIPCommand>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CheckContactExistsBySubjectIdAsync_ContactFound_ReturnsTrue()
        {
            var subjectGuid = System.Guid.NewGuid();
            _contactApiMock
                .Setup(x => x.ContactGetBySubjectIdAsync(subjectGuid, DxpTenantName))
                .ReturnsAsync(new ContactGetResultSuccessResult { Data = new ContactGetResult() });

            var result = await _service.CheckContactExistsBySubjectIdAsync(subjectGuid, DxpTenantName);

            Assert.True(result);
        }
    }
}
