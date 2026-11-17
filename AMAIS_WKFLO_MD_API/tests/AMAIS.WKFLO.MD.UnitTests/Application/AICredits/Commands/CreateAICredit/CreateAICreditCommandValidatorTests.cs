using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit;
using AMAIS.WKFLO.MD.Application.Common;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandValidatorTests
    {
        private const int ExistingAIModelId = 1;
        private const int AIModelIdWithExistingCredit = 2;
        private const int NonExistingAIModelId = 999;

        private readonly CreateAICreditCommandValidator _validator;

        public CreateAICreditCommandValidatorTests()
        {
            var tenantContext = new TenantContext { TenantName = "test-tenant" };

            var aiModels = new List<AIModel>
            {
                new() { Id = ExistingAIModelId, Name = "Model 1", Model = "gpt-4o", Provider = "azure", Type = "chat", IsActive = true },
                new() { Id = AIModelIdWithExistingCredit, Name = "Model 2", Model = "gpt-4o", Provider = "azure", Type = "chat", IsActive = true }
            }.BuildMockDbSet();

            var aiCredits = new List<AICredit>
            {
                new() { Id = 1, AIModelId = AIModelIdWithExistingCredit, TokensIn = 100, TokensOut = 100, MultiplierIn = 1, MultiplierOut = 1 }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aiModels.Object);
            db.Setup(x => x.AICredits).Returns(aiCredits.Object);

            _validator = new CreateAICreditCommandValidator(db.Object, tenantContext);
        }

        private static CreateAICreditCommand BuildCommand(int aiModelId, decimal multiplierIn = 1m, decimal multiplierOut = 1m, long tokensIn = 100, long tokensOut = 100, decimal costInMTok = 1m, decimal costOutMTok = 1m) => new()
        {
            AIModelId = aiModelId,
            TokensIn = tokensIn,
            CostInMTok = costInMTok,
            TokensOut = tokensOut,
            CostOutMTok = costOutMTok,
            MultiplierIn = multiplierIn,
            MultiplierOut = multiplierOut
        };

        [Fact]
        public void Validate_ValidCommand_IsValid()
        {
            var command = BuildCommand(ExistingAIModelId);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_MultiplierInNotPositive_ReturnsError(decimal multiplierIn)
        {
            var command = BuildCommand(ExistingAIModelId, multiplierIn: multiplierIn);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditMultiplierMustBePositive);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_MultiplierOutNotPositive_ReturnsError(decimal multiplierOut)
        {
            var command = BuildCommand(ExistingAIModelId, multiplierOut: multiplierOut);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditMultiplierMustBePositive);
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(0.99)]
        public void Validate_FractionalMultiplier_IsValid(decimal multiplier)
        {
            var command = BuildCommand(ExistingAIModelId, multiplierIn: multiplier, multiplierOut: multiplier);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_NegativeTokensIn_ReturnsError()
        {
            var command = BuildCommand(ExistingAIModelId, tokensIn: -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditTokensMustBeNonNegative);
        }

        [Fact]
        public void Validate_NegativeCostInMTok_ReturnsError()
        {
            var command = BuildCommand(ExistingAIModelId, costInMTok: -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditCostMustBeNonNegative);
        }

        [Fact]
        public void Validate_NonExistingAIModel_ReturnsError()
        {
            var command = BuildCommand(NonExistingAIModelId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AIModelMustExistForAICredit);
        }

        [Fact]
        public void Validate_AIModelAlreadyHasAICredit_ReturnsError()
        {
            var command = BuildCommand(AIModelIdWithExistingCredit);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditAlreadyExistsForAIModel);
        }
    }
}
