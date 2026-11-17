using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using AMAIS.WKFLO.MD.Application.Common;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommandValidatorTests
    {
        private readonly CreateAIModelCommandValidator _validator;

        public CreateAIModelCommandValidatorTests()
        {
            var tenantContext = new TenantContext { TenantName = "test-tenant" };
            var aiModels = new List<AIModel>().BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aiModels.Object);

            _validator = new CreateAIModelCommandValidator(db.Object, tenantContext);
        }

        private static CreateAIModelCommand BuildCommand(string provider, string authMode) => new()
        {
            Name = "Test Model",
            Model = "gpt-4o",
            Provider = provider,
            Type = "chat",
            IsActive = true,
            AuthMode = authMode
        };

        [Fact]
        public void Validate_ManagedIdentity_WithAzureProvider_IsValid()
        {
            var command = BuildCommand("azure", "managedIdentity");

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("openai")]
        [InlineData("anthropic")]
        [InlineData("google")]
        public void Validate_ManagedIdentity_WithNonAzureProvider_ReturnsAuthModeInvalidForProvider(string provider)
        {
            var command = BuildCommand(provider, "managedIdentity");

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AIModelAuthModeInvalidForProvider);
        }

        [Fact]
        public void Validate_ApiKey_WithNonAzureProvider_IsValid()
        {
            var command = BuildCommand("openai", "apiKey");

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_NullAuthMode_WithAnyProvider_IsValid()
        {
            var command = BuildCommand("openai", null);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }
    }
}
