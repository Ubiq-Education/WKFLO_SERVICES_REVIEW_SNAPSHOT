using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Tenants
{
    // CreateTenantCommandHandler.GetDefaultOrigins() reads the process-wide DEFAULT_WEB_ORIGINS
    // environment variable. CreateAgentChatSettingCommandHandlerTests and
    // UpdateAgentChatSettingCommandHandlerTests temporarily mutate that same variable, so this
    // class must share their collection to guarantee xUnit never runs them concurrently —
    // otherwise WebOrigins here randomly picks up whatever value those tests happen to have set.
    [Collection(DefaultWebOriginsEnvironmentCollection.Name)]
    public class CreateTenantCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string CreateUrl = "/api/v1/tenant";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task CreateFirstTenant_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantData();
            var command = GetCreateCommandFirstTenant();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            var expected = new SuccessResult<TenantCreateResult>(new TenantCreateResult()
            {
                Name = "TestTenant1",
                Region = "UK",
                WebOrigins = new List<TenantWebOriginResult>(),
                Code = 201
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.Guid = responseObject.Data.Guid;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Region);
            Assert.Equal(command.Region, successResult.Region);
        }

        [Fact]
        public async Task CreateSecondTenant_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantData();
            var command = GetCreateCommandSecondTenant();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

            var expected = new SuccessResult<TenantCreateResult>(new TenantCreateResult()
            {
                Name = "TestTenant2",
                Region = "US",
                WebOrigins = new List<TenantWebOriginResult>(),
                Code = 201
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.Guid = responseObject.Data.Guid;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Region);
            Assert.Equal(command.Region, successResult.Region);
        }

        [Fact]
        public async Task CreateTenantWithoutAuthorization_ResultUnauthorized()
        {
            // Arrange
            Fixture.DeleteTenantData();
            var command = GetCreateCommandFirstTenant();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");

            ClearHeader();
            // No authorization header

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        private static CreateTenantCommand GetCreateCommandFirstTenant()
        {
            return new CreateTenantCommand()
            {
                Name = "TestTenant1",
                Region = "UK"
            };
        }

        private static CreateTenantCommand GetCreateCommandSecondTenant()
        {
            return new CreateTenantCommand()
            {
                Name = "TestTenant2",
                Region = "US"
            };
        }
    }
}

