using AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.TriggerMultiAgentOrchestrator
{
    public class TriggerMultiAgentOrchestratorCommandValidatorTests
    {
        private const string TenantName = "test-tenant";
        private const int ActiveAgentId = 1;
        private const int InactiveAgentId = 2;
        private const int SoftDeletedAgentId = 3; // omitted from the mocked set — mirrors EF's global !IsDeleted filter making it invisible
        private const int NonExistentAgentId = 999;

        private readonly TriggerMultiAgentOrchestratorCommandValidator _validator;

        public TriggerMultiAgentOrchestratorCommandValidatorTests()
        {
            var tenantContext = new TenantContext { TenantName = TenantName };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var tenantAgents = new List<TenantAgent>
            {
                new() { Id = ActiveAgentId, Tenant = tenant, IsActive = true },
                new() { Id = InactiveAgentId, Tenant = tenant, IsActive = false }
                // SoftDeletedAgentId intentionally not included — a soft-deleted TenantAgent is
                // invisible to every EF query via the global !IsDeleted filter, so the real DbContext
                // would never return it here either.
            }.BuildMockDbSet();

            var publishedTenantAgents = new List<PublishedTenantAgent>().BuildMockDbSet();
            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedTenantAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            _validator = new TriggerMultiAgentOrchestratorCommandValidator(db.Object, tenantContext);
        }

        private static TriggerMultiAgentOrchestratorCommand BuildValidCommand() => new()
        {
            Input = new AgentInvocationInput { Text = "Do the task" },
            SessionId = "session-1",
            LeadAgent = new MultiAgentOrchestratorStep { AgentId = ActiveAgentId, AgentRole = "drafter" },
            FanOutAgents = new List<MultiAgentOrchestratorStep>
            {
                new() { AgentId = ActiveAgentId, AgentRole = "critic" }
            }
        };

        [Fact]
        public void Validate_AllAgentsExistAndActive_IsValid()
        {
            var command = BuildValidCommand();

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_LeadAgentInactive_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand();
            command.LeadAgent = new MultiAgentOrchestratorStep { AgentId = InactiveAgentId, AgentRole = "drafter" };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_LeadAgentDoesNotExist_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand();
            command.LeadAgent = new MultiAgentOrchestratorStep { AgentId = NonExistentAgentId, AgentRole = "drafter" };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_LeadAgentSoftDeleted_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand();
            command.LeadAgent = new MultiAgentOrchestratorStep { AgentId = SoftDeletedAgentId, AgentRole = "drafter" };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_FanOutAgentDoesNotExist_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand();
            command.FanOutAgents = new List<MultiAgentOrchestratorStep>
            {
                new() { AgentId = NonExistentAgentId, AgentRole = "critic" }
            };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_DuplicateAgentRolesBetweenLeadAndFanOut_ReturnsAgentRoleMustBeUnique()
        {
            var command = BuildValidCommand();
            command.LeadAgent = new MultiAgentOrchestratorStep { AgentId = ActiveAgentId, AgentRole = "drafter" };
            command.FanOutAgents = new List<MultiAgentOrchestratorStep>
            {
                new() { AgentId = ActiveAgentId, AgentRole = "drafter" } // duplicate of the lead role
            };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentRoleMustBeUnique);
        }

        [Fact]
        public void Validate_DuplicateAgentRolesAmongFanOutAgents_ReturnsAgentRoleMustBeUnique()
        {
            var command = BuildValidCommand();
            command.FanOutAgents = new List<MultiAgentOrchestratorStep>
            {
                new() { AgentId = ActiveAgentId, AgentRole = "critic" },
                new() { AgentId = ActiveAgentId, AgentRole = "critic" }
            };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentRoleMustBeUnique);
        }

        [Fact]
        public void Validate_FanOutAgentsEmpty_ReturnsRequired()
        {
            var command = BuildValidCommand();
            command.FanOutAgents = new List<MultiAgentOrchestratorStep>();

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.Required);
        }
    }
}
