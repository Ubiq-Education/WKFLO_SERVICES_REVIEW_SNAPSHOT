using AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.ContactFormSubmitted
{
    public class ReportContactFormSubmittedDomainEventHandlerTests
    {
        private readonly Mock<IDxpContactFormService> _dxpServiceMock;

        public ReportContactFormSubmittedDomainEventHandlerTests()
        {
            _dxpServiceMock = new Mock<IDxpContactFormService>();
            _dxpServiceMock
                .Setup(x => x.ReportContactFormSubmissionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        private ReportContactFormSubmittedDomainEventHandler BuildHandler(
            IEnumerable<DxpIntegration> integrations, IEnumerable<TenantAgent> tenantAgents = null, IEnumerable<PublishedTenantAgent> publishedTenantAgents = null,
            IEnumerable<AgentChatSetting> agentChatSettings = null)
        {
            var dbSet = integrations.ToList().BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.DxpIntegrations).Returns(dbSet.Object);
            db.Setup(x => x.TenantAgents).Returns((tenantAgents ?? []).ToList().BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantAgents).Returns((publishedTenantAgents ?? []).ToList().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((agentChatSettings ?? []).ToList().BuildMockDbSet().Object);
            return new ReportContactFormSubmittedDomainEventHandler(db.Object, _dxpServiceMock.Object);
        }

        private static DxpIntegration BuildActiveIntegration() => new()
        {
            Id = 1,
            TenantId = 1,
            Tenant = new Tenant { Id = 1, Name = "test-tenant" },
            DxpTenantName = "dxp-tenant",
            Domain = "https://dxp.example.com",
            ApiKey = "api-key-123",
            IsActive = true,
            IsDeleted = false
        };

        [Fact]
        public async Task Handle_IntegrationActiveAndExists_CallsService()
        {
            var handler = BuildHandler(new[] { BuildActiveIntegration() });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com");

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                "dxp-tenant", "https://dxp.example.com", "api-key-123",
                "sub-1", "sess-1", "John", "Doe", "john@example.com",
                null, null, null),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TenantScopedAgentId_ResolvesTenantAndPublishedAgentIds()
        {
            // The tenant-scoped submission path (SubmitContactFormCommandHandler) publishes a TenantAgent.Id
            // as the bare AgentId — the handler must resolve the sibling PublishedTenantAgentId and forward
            // both, since ObjectMetadata.Metadata carrying the agent identity is what fixes the
            // always-zero leads-generated KPI.
            var tenantAgent = new TenantAgent { Id = 5, PublishedTenantAgentId = 9 };
            var handler = BuildHandler(new[] { BuildActiveIntegration() }, tenantAgents: new[] { tenantAgent });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com", shareId: "share-abc", agentId: 5);

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                "dxp-tenant", "https://dxp.example.com", "api-key-123",
                "sub-1", "sess-1", "John", "Doe", "john@example.com",
                5, 9, "share-abc"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_PublicAgentPathWithOnlyShareId_ForwardsShareIdWithNullAgentIdsWhenNoMappingExists()
        {
            // No AgentChatSetting matches "share-xyz" — resolution finds nothing, so the ids stay null
            // rather than throwing.
            var handler = BuildHandler(new[] { BuildActiveIntegration() });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com", shareId: "share-xyz");

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                null, null, "share-xyz"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_PublicAgentPathWithOnlyShareId_ResolvesTenantAndPublishedAgentIdsFromMapping()
        {
            // Regression guard for the always-zero leads-generated KPI: a contact form submitted via the
            // public/share widget only knows ShareId at submission time, but CMS's Analytics dashboard only
            // ever queries by TenantAgentId — so the event must resolve and carry all 3 identifiers together,
            // exactly like every other chatbot analytics event does, or it becomes invisible to that dashboard.
            var tenantAgent = new TenantAgent { Id = 5, PublishedTenantAgentId = 9 };
            var chatSetting = new AgentChatSetting { AgentId = 5, ShareId = "share-xyz" };
            var handler = BuildHandler(new[] { BuildActiveIntegration() }, tenantAgents: new[] { tenantAgent }, agentChatSettings: new[] { chatSetting });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com", shareId: "share-xyz");

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                "dxp-tenant", "https://dxp.example.com", "api-key-123",
                "sub-1", "sess-1", "John", "Doe", "john@example.com",
                5, 9, "share-xyz"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TenantScopedAgentIdWithoutShareId_ResolvesShareIdFromAgentChatSetting()
        {
            // Same gap in the other direction: the tenant-scoped path only knows AgentId, but must still
            // resolve ShareId when a mapping exists, rather than leaving it permanently null.
            var tenantAgent = new TenantAgent { Id = 5, PublishedTenantAgentId = 9 };
            var chatSetting = new AgentChatSetting { AgentId = 5, ShareId = "share-abc" };
            var handler = BuildHandler(new[] { BuildActiveIntegration() }, tenantAgents: new[] { tenantAgent }, agentChatSettings: new[] { chatSetting });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com", agentId: 5);

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                "dxp-tenant", "https://dxp.example.com", "api-key-123",
                "sub-1", "sess-1", "John", "Doe", "john@example.com",
                5, 9, "share-abc"),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NoIntegrationForTenant_DoesNotCallService()
        {
            var handler = BuildHandler(Array.Empty<DxpIntegration>());
            var notification = new ContactFormSubmittedDomainEvent(
                "no-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com");

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_IntegrationInactive_DoesNotCallService()
        {
            var tenant = new Tenant { Id = 1, Name = "test-tenant" };
            var integration = new DxpIntegration
            {
                Id = 1,
                TenantId = 1,
                Tenant = tenant,
                DxpTenantName = "dxp-tenant",
                Domain = "https://dxp.example.com",
                ApiKey = "api-key-123",
                IsActive = false,
                IsDeleted = false
            };
            var handler = BuildHandler(new[] { integration });
            var notification = new ContactFormSubmittedDomainEvent(
                "test-tenant", "sub-1", "sess-1", "John", "Doe", "john@example.com");

            // Act
            await handler.Handle(notification, TestContext.Current.CancellationToken);

            // Assert
            _dxpServiceMock.Verify(x => x.ReportContactFormSubmissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>()),
                Times.Never);
        }
    }
}
