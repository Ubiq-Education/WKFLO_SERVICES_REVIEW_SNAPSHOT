using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit;
using AMAIS.WKFLO.MD.Application.Common;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommandValidatorTests
    {
        private const int ExistingAICreditId = 1;
        private const int ExistingAICreditAIModelId = 1;

        private const int OtherAICreditId = 2;
        private const int OtherAICreditAIModelId = 2;

        private const int AIModelIdWithNoCredit = 3;
        private const int NonExistingAIModelId = 999;
        private const int NonExistingAICreditId = 999;

        private readonly UpdateAICreditCommandValidator _validator;

        public UpdateAICreditCommandValidatorTests()
        {
            var tenantContext = new TenantContext { TenantName = "test-tenant" };

            var aiModels = new List<AIModel>
            {
                new() { Id = ExistingAICreditAIModelId, Name = "Model 1", Model = "gpt-4o", Provider = "azure", Type = "chat", IsActive = true },
                new() { Id = OtherAICreditAIModelId, Name = "Model 2", Model = "gpt-4o", Provider = "azure", Type = "chat", IsActive = true },
                new() { Id = AIModelIdWithNoCredit, Name = "Model 3", Model = "gpt-4o", Provider = "azure", Type = "chat", IsActive = true }
            }.BuildMockDbSet();

            var aiCredits = new List<AICredit>
            {
                new() { Id = ExistingAICreditId, AIModelId = ExistingAICreditAIModelId, TokensIn = 100, TokensOut = 100, MultiplierIn = 1, MultiplierOut = 1 },
                new() { Id = OtherAICreditId, AIModelId = OtherAICreditAIModelId, TokensIn = 100, TokensOut = 100, MultiplierIn = 1, MultiplierOut = 1 }
            }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AIModels).Returns(aiModels.Object);
            db.Setup(x => x.AICredits).Returns(aiCredits.Object);

            _validator = new UpdateAICreditCommandValidator(db.Object, tenantContext);
        }

        private static UpdateAICreditCommand BuildCommand(int id, int aiModelId, decimal multiplierIn = 1m, decimal multiplierOut = 1m, long tokensIn = 100, long tokensOut = 100, decimal costInMTok = 1m, decimal costOutMTok = 1m) => new()
        {
            Id = id,
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
            var command = BuildCommand(ExistingAICreditId, ExistingAICreditAIModelId);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_NonExistingAICreditId_ReturnsError()
        {
            var command = BuildCommand(NonExistingAICreditId, ExistingAICreditAIModelId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditMustExist);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_MultiplierInNotPositive_ReturnsError(decimal multiplierIn)
        {
            var command = BuildCommand(ExistingAICreditId, ExistingAICreditAIModelId, multiplierIn: multiplierIn);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditMultiplierMustBePositive);
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(0.99)]
        public void Validate_FractionalMultiplier_IsValid(decimal multiplier)
        {
            var command = BuildCommand(ExistingAICreditId, ExistingAICreditAIModelId, multiplierIn: multiplier, multiplierOut: multiplier);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_NegativeTokensOut_ReturnsError()
        {
            var command = BuildCommand(ExistingAICreditId, ExistingAICreditAIModelId, tokensOut: -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditTokensMustBeNonNegative);
        }

        [Fact]
        public void Validate_NonExistingAIModel_ReturnsError()
        {
            var command = BuildCommand(ExistingAICreditId, NonExistingAIModelId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AIModelMustExistForAICredit);
        }

        [Fact]
        public void Validate_ChangingToAIModelIdUsedByAnotherAICredit_ReturnsError()
        {
            var command = BuildCommand(ExistingAICreditId, OtherAICreditAIModelId);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == ErrorConst.AICreditAlreadyExistsForAIModel);
        }

        [Fact]
        public void Validate_ChangingToAIModelIdWithNoExistingCredit_IsValid()
        {
            var command = BuildCommand(ExistingAICreditId, AIModelIdWithNoCredit);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_KeepingSameAIModelId_IsValid()
        {
            var command = BuildCommand(ExistingAICreditId, ExistingAICreditAIModelId);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }
    }
}
