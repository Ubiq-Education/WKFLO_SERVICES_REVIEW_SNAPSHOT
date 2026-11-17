using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret;
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
    public class UpdateSecretCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string UpdateUrl = "/api/v1/secret";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task UpdateSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecret();
            var successArrangeResult = (SuccessResult<SecretCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandSecret(successArrangeResult.Data.Id);

            var json = JsonConvert.SerializeObject(command2);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<SecretUpdateResult>(new SecretUpdateResult()
            {
                Id = successArrangeResult.Data.Id,
                ApiKey = "Test ApiKey 2",
                Name = "Test Endpoint 2",
                IsActive = false
            });

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/{successArrangeResult.Data.Id}", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<SecretUpdateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            expected.Data.LastUpdatedOn = responseObject.Data.LastUpdatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command2.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.Equal(command2.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task UpdateSecret_ResultInvalid_SecretMustExist()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command1 = GetUpdateCommandSecret(1);

            var json = JsonConvert.SerializeObject(command1);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult(nameof(Secret), ErrorConst.SecretMustExist);

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/1", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.SecretMustExist && m.Property == nameof(Secret));
        }

        private static CreateSecretCommand GetCreateCommandSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }

        private static UpdateSecretCommand GetUpdateCommandSecret(int id)
        {
            return new UpdateSecretCommand
            {
                Id = id,
                ApiKey = "Test ApiKey 2",
                Name = "Test Endpoint 2",
                IsActive = false
            };
        }

        private static UpdateSecretCommand GetUpdateCommandNotExistingSecret()
        {
            return new UpdateSecretCommand
            {
                Id = 1,
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }
    }
}

