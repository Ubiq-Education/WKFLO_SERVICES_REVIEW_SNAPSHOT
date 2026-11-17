using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.InvokeAgent
{
    public class InvokeAgentCommandValidatorTests
    {
        private const string TenantName = "test-tenant";
        private const int ActiveAgentId = 1;
        private const int InactiveAgentId = 2;
        private const int SoftDeletedAgentId = 3; // omitted from the mocked set — mirrors EF's global !IsDeleted filter making it invisible
        private const int NonExistentAgentId = 999;

        private readonly InvokeAgentCommandValidator _validator;
        private readonly TenantContext _tenantContext;

        public InvokeAgentCommandValidatorTests()
        {
            _tenantContext = new TenantContext { TenantName = TenantName };
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

            _validator = new InvokeAgentCommandValidator(db.Object, _tenantContext);
        }

        private static InvokeAgentCommand BuildValidCommand(int id) => new()
        {
            Id = id,
            AgentRole = "researcher",
            Input = new AgentInvocationInput { Text = "Research this topic" },
            SessionId = "session-1"
        };

        [Fact]
        public void Validate_ExistingActiveAgent_IsValid()
        {
            var command = BuildValidCommand(ActiveAgentId);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_InactiveAgent_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand(InactiveAgentId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_NonExistentAgent_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand(NonExistentAgentId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_SoftDeletedAgent_ReturnsAgentNotRunnable()
        {
            var command = BuildValidCommand(SoftDeletedAgentId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AgentNotRunnable);
        }

        [Fact]
        public void Validate_NullInput_ReturnsRequired()
        {
            var command = BuildValidCommand(ActiveAgentId);
            command.Input = null;

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.Required);
        }

        [Fact]
        public void Validate_EmptyInputText_ReturnsRequired()
        {
            var command = BuildValidCommand(ActiveAgentId);
            command.Input = new AgentInvocationInput { Text = "" };

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.Required);
        }

        [Fact]
        public void Validate_NoTenantInContext_ReturnsTenantInvalid()
        {
            var tenantContextWithoutTenant = new TenantContext { TenantName = null };
            var tenant = new Tenant { Id = 1, Name = TenantName };

            var tenantAgents = new List<TenantAgent>
            {
                new() { Id = ActiveAgentId, Tenant = tenant, IsActive = true }
            }.BuildMockDbSet();
            var publishedTenantAgents = new List<PublishedTenantAgent>().BuildMockDbSet();
            var tenants = new List<Tenant> { tenant }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantAgents).Returns(tenantAgents.Object);
            db.Setup(x => x.PublishedTenantAgents).Returns(publishedTenantAgents.Object);
            db.Setup(x => x.Tenants).Returns(tenants.Object);

            var validator = new InvokeAgentCommandValidator(db.Object, tenantContextWithoutTenant);
            var command = BuildValidCommand(ActiveAgentId);

            var result = validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.TenantInvalid);
        }
    }
}
