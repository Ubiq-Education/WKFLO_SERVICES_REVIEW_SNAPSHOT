using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Secrets
{
    public class ListSecretQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string ListUrl = "/api/v1/secrets";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task ListSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecret();
            var successArrangeResult = (SuccessResult<SecretCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<List<SecretGetResult>>(
            [
                new SecretGetResult()
                {
                    Id = successArrangeResult.Data.Id,
                    ApiKey = "Test ApiKey",
                    Name = "Test Endpoint",
                    IsActive = true
                }
            ]);

            // Act
            var response = await Fixture.Client.GetAsync($"{ListUrl}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObjects = JsonConvert.DeserializeObject<SuccessResult<List<SecretGetResult>>>(responseBody);
            expected.Data[0].Id = responseObjects.Data[0].Id;
            expected.Data[0].CreatedOn = responseObjects.Data[0].CreatedOn;
            responseObjects.Should().BeEquivalentTo(expected);
            var successResult = responseObjects.Data[0];
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.Equal(command.IsActive, successResult.IsActive);
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
    }
}

