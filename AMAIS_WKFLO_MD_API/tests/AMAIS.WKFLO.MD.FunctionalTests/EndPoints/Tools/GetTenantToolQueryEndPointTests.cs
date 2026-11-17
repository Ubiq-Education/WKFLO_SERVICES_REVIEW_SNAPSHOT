using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Tools
{
    public class GetTenantToolQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;
        private const string GetUrl = "/api/v1/tenant/tool";


        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task GetTenantTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandTenantTool();
            var successArrangeResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantToolGetResult>(new TenantToolGetResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            });

            // Act
            var response = await Fixture.Client.GetAsync($"{GetUrl}/{successArrangeResult.Data.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantToolGetResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command.Type, successResult.Type);
            Assert.NotNull(successResult.Config);
            Assert.Equal(command.Config, successResult.Config);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task GetTenantTool_ResultInvalid_TenantToolMustExist()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var command = GetQueryNotExistingTenantTool();

            var token = TokenBuilder.BuildJWTToken();
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new NotFoundResult();

            // Act
            var response = await Fixture.Client.GetAsync($"{GetUrl}/{command.Id}", TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var notFoundResult = JsonConvert.DeserializeObject<NotFoundResult>(responseBody);
            notFoundResult.Should().BeEquivalentTo(expected);
            // Assert
            Assert.IsType<NotFoundResult>(notFoundResult);
        }

        private static CreateTenantToolCommand GetCreateCommandTenantTool()
        {
            return new CreateTenantToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            };
        }

        private static GetTenantToolByIdQuery GetQueryNotExistingTenantTool()
        {
            return new GetTenantToolByIdQuery
            {
                Id = 0
            };
        }
    }
}

