using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.RateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Marketplace.Tools
{
    public class MarketplaceToolRateEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string RateUrl = "/api/v1/marketplace/tool";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task RateMarketplaceTool_WithValidRating_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(5);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<bool>>(responseBody);

            Assert.NotNull(responseObject);
            Assert.True(responseObject.Data);
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
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(rating);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<bool>>(responseBody);

            Assert.NotNull(responseObject);
            Assert.True(responseObject.Data);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        [InlineData(-1)]
        [InlineData(10)]
        public async Task RateMarketplaceTool_WithInvalidRating_ResultInvalid(int invalidRating)
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(invalidRating);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.ToolRatingMustBeBetween1And5);
        }

        [Fact]
        public async Task RateMarketplaceTool_WithNonExistentTool_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var rateCommand = GetRateMarketplaceToolCommand(5);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/999/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.ToolNotFound);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task RateMarketplaceTool_WithInvalidId_ResultInvalid(int invalidId)
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var rateCommand = GetRateMarketplaceToolCommand(5);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{invalidId}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.MustBePositive);
        }

        [Fact]
        public async Task RateMarketplaceTool_WithoutAuthentication_ResultUnauthorized()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(5);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");

            ClearHeader();
            // No authorization header

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task RateMarketplaceTool_WithMissingRating_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Send empty request body
            using var data = new StringContent("{}", Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.ToolRatingMustBeBetween1And5);
        }

        [Fact]
        public async Task RateMarketplaceTool_WithDeletedTool_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Manually mark the tool as deleted in database
            var db = Fixture.GetDbContext();
            var marketplaceTool = await db.MarketplaceTools.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            marketplaceTool.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceToolCommand(5);
            var json = JsonConvert.SerializeObject(rateCommand);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.ToolNotFound);
        }

        [Fact]
        public async Task RateMarketplaceTool_MultipleRatingsSameUser_ShouldUpdateExistingRating()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceToolCommand();
            var createResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // First rating
            var firstRateCommand = GetRateMarketplaceToolCommand(3);
            var firstJson = JsonConvert.SerializeObject(firstRateCommand);
            using var firstData = new StringContent(firstJson, Encoding.UTF8, "application/json");

            // Second rating (same user, different rating)
            var secondRateCommand = GetRateMarketplaceToolCommand(5);
            var secondJson = JsonConvert.SerializeObject(secondRateCommand);
            using var secondData = new StringContent(secondJson, Encoding.UTF8, "application/json");

            // Act
            var firstResponse = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", firstData, TestContext.Current.CancellationToken);
            var secondResponse = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", secondData, TestContext.Current.CancellationToken);

            // Assert
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstResponseBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var firstResult = JsonConvert.DeserializeObject<SuccessResult<bool>>(firstResponseBody);

            var secondResponseBody = await secondResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var secondResult = JsonConvert.DeserializeObject<SuccessResult<bool>>(secondResponseBody);

            Assert.True(firstResult.Data);
            Assert.True(secondResult.Data);
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

        private static RateMarketplaceToolCommand GetRateMarketplaceToolCommand(int rating)
        {
            return new RateMarketplaceToolCommand
            {
                Rating = rating
            };
        }
    }
}
