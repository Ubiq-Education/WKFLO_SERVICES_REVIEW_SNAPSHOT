using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Commands
{
    public class RateMarketplaceToolCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public RateMarketplaceToolCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RateMarketplaceTool_WithValidRating_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 5);

            // Act
            var result = (Result)await mediator.Send(rateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            var successResult = ((SuccessResult<bool>)result).Data;
            Assert.True(successResult);

            // Verify UserStar was created in database
            var db = _fixture.GetDbContext();
            var userStar = await db.UserStars.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(userStar);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task RateMarketplaceTool_WithValidRatingRange_ResultOk(int rating)
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext($"test{rating}@example.com", $"TestUser{rating}", $"test-user-id-{rating}");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, rating);

            // Act
            var result = (Result)await mediator.Send(rateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            var successResult = ((SuccessResult<bool>)result).Data;
            Assert.True(successResult);
        }

        [Fact]
        public async Task RateMarketplaceTool_WithNonExistentTool_ResultInvalid()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var rateCommand = GetRateMarketplaceToolCommand(999, 5);

            // Act
            var result = (Result)await mediator.Send(rateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var notFoundResult = (InvalidResult)result;
            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, e => e.Code == ErrorConst.ToolNotFound && e.Property == nameof(MarketplaceTool));
        }

        [Fact]
        public async Task RateMarketplaceTool_WithDeletedTool_ResultInvalid()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Mark tool as deleted
            var db = _fixture.GetDbContext();
            var tool = await db.MarketplaceTools.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            tool.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 5);

            // Act
            var result = (Result)await mediator.Send(rateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var notFoundResult = (InvalidResult)result;
            Assert.NotEmpty(notFoundResult.Errors);
            Assert.Contains(notFoundResult.Errors, e => e.Code == ErrorConst.ToolNotFound && e.Property == nameof(MarketplaceTool));
        }

        [Fact]
        public async Task RateMarketplaceTool_UpdateExistingRating_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // First rating
            var firstRateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 3);
            var firstResult = (Result)await mediator.Send(firstRateCommand, TestContext.Current.CancellationToken);

            // Second rating (update)
            var secondRateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 5);

            // Act
            var result = (Result)await mediator.Send(secondRateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            var successResult = ((SuccessResult<bool>)result).Data;
            Assert.True(successResult);

            // Verify that only one UserStar exists with updated rating
            var db = _fixture.GetDbContext();
            var userStars = db.UserStars.Where(us => us.Email == "test@example.com" &&
                                                   us.TypeId == createResult.Data.Id &&
                                                   us.Type == nameof(MarketplaceTool)).ToList();
            Assert.Single(userStars);
            Assert.Equal(5, userStars.First().Value);
        }

        [Fact]
        public async Task RateMarketplaceTool_MultipleUsers_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceToolData();
            _fixture.DeleteUserStarData();
            _fixture.DeleteUserData();
            _fixture.AddTenantContext("Test");
            _fixture.AddUserContext("test@example.com", "TestUser", "test-user-id");

            var mediator = _fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // First user rating
            _fixture.AddUserContext("user1@example.com", "User1", "user-1-id");
            var firstRateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 4);
            var firstResult = (Result)await mediator.Send(firstRateCommand, TestContext.Current.CancellationToken);

            // Second user rating
            _fixture.AddUserContext("user2@example.com", "User2", "user-2-id");
            var secondRateCommand = GetRateMarketplaceToolCommand(createResult.Data.Id, 5);

            // Act
            var result = (Result)await mediator.Send(secondRateCommand, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            var successResult = ((SuccessResult<bool>)result).Data;
            Assert.True(successResult);

            // Verify that both UserStars exist
            var db = _fixture.GetDbContext();
            var userStars = db.UserStars.Where(us => us.TypeId == createResult.Data.Id &&
                                                   us.Type == nameof(MarketplaceTool)).ToList();
            Assert.Equal(2, userStars.Count);
        }

        private static CreateMarketplaceToolCommand GetCreateMarketplaceToolCommand()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Marketplace Tool for Rating",
                Description = "Test Marketplace Tool Description for Rating",
                Type = "API",
                Config = "{\"endpoint\": \"https://api.example.com/rating-test\"}"
            };
        }

        private static RateMarketplaceToolCommand GetRateMarketplaceToolCommand(int id, int rating)
        {
            return new RateMarketplaceToolCommand
            {
                Id = id,
                Rating = rating
            };
        }
    }
}