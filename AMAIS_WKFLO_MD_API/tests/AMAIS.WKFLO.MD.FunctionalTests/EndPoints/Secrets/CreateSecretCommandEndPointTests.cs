using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Secrets
{
    public class CreateSecretCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string CreateUrl = "/api/v1/secret";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task CreateFirstSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            var command = GetCreateCommandFirstSecret();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<SecretCreateResult>(new SecretCreateResult()
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<SecretCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSecondSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            var command = GetCreateCommandSecondSecret();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<SecretCreateResult>(new SecretCreateResult()
            {
                ApiKey = "Test ApiKey 2",
                Name = "Test Endpoint 2",
                IsActive = true
            });

            // Act
            var response = await Fixture.Client.PostAsync(CreateUrl, data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<SecretCreateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        private static CreateSecretCommand GetCreateCommandFirstSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }

        private static CreateSecretCommand GetCreateCommandSecondSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey 2",
                Name = "Test Endpoint 2",
                IsActive = true
            };
        }
    }
}

