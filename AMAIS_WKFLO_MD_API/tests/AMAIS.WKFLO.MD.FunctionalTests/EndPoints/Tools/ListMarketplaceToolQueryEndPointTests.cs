using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Tools
{
    public class ListMarketplaceToolQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string ListUrl = "/api/v1/marketplace/tools";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task ListMarketplaceTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandMarketplaceTool();
            var successArrangeResult = (SuccessResult<MarketplaceToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            //Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<List<MarketplaceToolGetResult>>(
            [
                new MarketplaceToolGetResult()
                {
                    Id = successArrangeResult.Data.Id,
                    Name = "Test Name",
                    Description = "Test Description",
                    Type = "Test Type",
                    Config = "Test Config"
                }
            ]);

            // Act
            var response = await Fixture.Client.GetAsync($"{ListUrl}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObjects = JsonConvert.DeserializeObject<SuccessResult<List<MarketplaceToolGetResult>>>(responseBody);
            expected.Data[0].Id = responseObjects.Data[0].Id;
            expected.Data[0].CreatedOn = responseObjects.Data[0].CreatedOn;
            responseObjects.Should().BeEquivalentTo(expected);
            var successResult = responseObjects.Data[0];
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
        }

        private static CreateMarketplaceToolCommand GetCreateCommandMarketplaceTool()
        {
            return new CreateMarketplaceToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config"
            };
        }
    }
}

