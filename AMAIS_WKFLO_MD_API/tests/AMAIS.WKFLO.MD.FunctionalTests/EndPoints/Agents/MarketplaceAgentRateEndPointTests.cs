using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Marketplace.Agents
{
    public class MarketplaceAgentRateEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string RateUrl = "/api/v1/marketplace/agent";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task RateMarketplaceAgent_WithValidRating_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceAgentCommand(5);
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
        public async Task RateMarketplaceAgent_WithValidRatingRange_ResultOk(int rating)
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceAgentCommand(rating);
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
        public async Task RateMarketplaceAgent_WithInvalidRating_ResultInvalid(int invalidRating)
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceAgentCommand(invalidRating);
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
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.AgentRatingMustBeBetween1And5);
        }

        [Fact]
        public async Task RateMarketplaceAgent_WithNonExistentAgent_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");

            var rateCommand = GetRateMarketplaceAgentCommand(5);
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
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.AgentNotFound);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task RateMarketplaceAgent_WithInvalidId_ResultInvalid(int invalidId)
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");

            var rateCommand = GetRateMarketplaceAgentCommand(5);
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
        public async Task RateMarketplaceAgent_WithoutAuthentication_ResultUnauthorized()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceAgentCommand(5);
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
        public async Task RateMarketplaceAgent_WithMissingRating_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Send empty request body
            using var data = new StringContent("{}", Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            // Act
            var response = await Fixture.Client.PostAsync($"{RateUrl}/{createResult.Data.Id}/rate", data, TestContext.Current.CancellationToken);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);

            Assert.NotNull(invalidResult);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.AgentRatingMustBeBetween1And5);
        }

        [Fact]
        public async Task RateMarketplaceAgent_WithDeletedAgent_ResultInvalid()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");

            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            // Manually mark the agent as deleted in database
            var db = Fixture.GetDbContext();
            var marketplaceAgent = await db.MarketplaceAgents.FirstOrDefaultAsync(m => m.Id == createResult.Data.Id, TestContext.Current.CancellationToken);
            marketplaceAgent.IsDeleted = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var rateCommand = GetRateMarketplaceAgentCommand(5);
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
            Assert.Contains(invalidResult.Errors, e => e.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task RateMarketplaceAgent_MultipleRatingsSameUser_ShouldUpdateExistingRating()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var createCommand = GetCreateMarketplaceAgentCommand();
            var createResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test2");

            // First rating
            var firstRateCommand = GetRateMarketplaceAgentCommand(3);
            var firstJson = JsonConvert.SerializeObject(firstRateCommand);
            using var firstData = new StringContent(firstJson, Encoding.UTF8, "application/json");

            // Second rating (same user, different rating)
            var secondRateCommand = GetRateMarketplaceAgentCommand(5);
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

        private static CreateMarketplaceAgentCommand GetCreateMarketplaceAgentCommand()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Marketplace Agent for Rating",
                Description = "Test Marketplace Agent Description for Rating",
                SystemPrompt = "Test System Prompt for Rating"
            };
        }

        private static RateMarketplaceAgentCommand GetRateMarketplaceAgentCommand(int rating)
        {
            return new RateMarketplaceAgentCommand
            {
                Rating = rating
            };
        }
    }
}
