using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.Tools
{
    public class UpdateTenantToolCommandEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        private const string UpdateUrl = "/api/v1/tenant/tool";

        private void ClearHeader()
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
        }

        [Fact]
        public async Task UpdateTenantTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandTenantTool();
            var successArrangeResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandTenantTool(successArrangeResult.Data.Id);

            var json = JsonConvert.SerializeObject(command2);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new SuccessResult<TenantToolUpdateResult>(new TenantToolUpdateResult()
            {
                Id = successArrangeResult.Data.Id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2",
                IsActive = false
            });

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/{successArrangeResult.Data.Id}", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var responseObject = JsonConvert.DeserializeObject<SuccessResult<TenantToolUpdateResult>>(responseBody);
            expected.Data.Id = responseObject.Data.Id;
            expected.Data.CreatedOn = responseObject.Data.CreatedOn;
            expected.Data.LastUpdatedOn = responseObject.Data.LastUpdatedOn;
            responseObject.Should().BeEquivalentTo(expected);
            var successResult = responseObject.Data;
            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command2.Description, successResult.Description);
            Assert.NotNull(successResult.Type);
            Assert.Equal(command2.Type, successResult.Type);
            Assert.NotNull(successResult.Config);
            Assert.Equal(command2.Config, successResult.Config);
            Assert.Equal(command2.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task UpdateTenantTool_ResultInvalid_TenantToolMustExist()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command1 = GetUpdateCommandTenantTool(1);

            var json = JsonConvert.SerializeObject(command1);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult(nameof(Tool), ErrorConst.ToolMustExist);

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/1", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.ToolMustExist && m.Property == nameof(Tool));
        }

        [Fact]
        public async Task UpdateSameSecondTenantTool_ResultInvalid_ThereIsNoTenant_And_Tool()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            var command = GetUpdateCommandNotExistingTenantTool();

            var json = JsonConvert.SerializeObject(command);
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            var token = TokenBuilder.BuildJWTToken();

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
            // No headers for Tenant Context
            //_fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            var expected = new InvalidResult();
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid);
            expected.AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);
            expected.AddError(nameof(Tool), ErrorConst.ToolMustExist);

            // Act
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/1", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var invalidResult = JsonConvert.DeserializeObject<InvalidResult>(responseBody);
            invalidResult.Should().BeEquivalentTo(expected);
            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantMustExist && m.Property == nameof(TenantContext.TenantName));
        }

        // US 7849: GET never returns the stored OAuth client secret (only hasClientSecret), and a PUT that
        // resends the config without it — exactly what the UI does — keeps the stored secret and the existing
        // OAuth connections. Seeds an already-encrypted value directly, so neither request needs the
        // encryption key (the keep path never encrypts or decrypts).
        [Fact]
        public async Task UpdateTenantTool_McpOAuthClientSecret_StrippedOnGet_KeptOnPutWithoutSecret()
        {
            // Arrange
            const string storedSecret = "enc:v1:ZmFrZS1jaXBoZXJ0ZXh0";
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var created = (SuccessResult<TenantToolCreateResult>)await mediator.Send(new CreateTenantToolCommand
            {
                Name = "Canva",
                Description = "Canva MCP",
                Type = CommonConsts.CommonToolTypeConsts.MCP,
                Config = McpOAuthConfig(clientSecret: null),
                IsActive = true
            }, TestContext.Current.CancellationToken);
            var toolId = created.Data.Id;
            var connectionId = created.Data.McpOAuthConnectionId.Value;

            var db = Fixture.GetDbContext();
            var entity = db.TenantTools.First(t => t.Id == toolId);
            var seeded = JObject.Parse(entity.Config);
            seeded["clientSecret"] = storedSecret;
            entity.Config = seeded.ToString(Formatting.None);
            db.ToolOAuthTokens.Add(new ToolOAuthToken { McpOAuthConnectionId = connectionId, AccessToken = "x", ConnectedOnUtc = System.DateTimeOffset.UtcNow, TokenObtainedAtUtc = System.DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act 1 — GET
            var getResponse = await Fixture.Client.GetAsync($"{UpdateUrl}/{toolId}", TestContext.Current.CancellationToken);

            // Assert 1
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var getBody = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            getBody.Should().NotContain(storedSecret);
            var getConfig = JObject.Parse(JsonConvert.DeserializeObject<SuccessResult<TenantToolGetResult>>(getBody).Data.Config);
            getConfig.ContainsKey("clientSecret").Should().BeFalse();
            getConfig.Value<bool>("hasClientSecret").Should().BeTrue();

            // Act 2 — PUT the config back as the UI would (hasClientSecret echoed, no clientSecret)
            var update = new UpdateTenantToolCommand
            {
                Id = toolId,
                Name = "Canva",
                Description = "Canva MCP renamed",
                Type = CommonConsts.CommonToolTypeConsts.MCP,
                Config = getConfig.ToString(Formatting.None),
                IsActive = true
            };
            using var data = new StringContent(JsonConvert.SerializeObject(update), Encoding.UTF8, "application/json");
            var putResponse = await Fixture.Client.PutAsync($"{UpdateUrl}/{toolId}", data, TestContext.Current.CancellationToken);

            // Assert 2
            putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var putBody = await putResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            putBody.Should().NotContain(storedSecret);
            JObject.Parse(JsonConvert.DeserializeObject<SuccessResult<TenantToolUpdateResult>>(putBody).Data.Config)
                .Value<bool>("hasClientSecret").Should().BeTrue();

            var persisted = JObject.Parse(db.TenantTools.AsNoTracking().First(t => t.Id == toolId).Config);
            persisted.Value<string>("clientSecret").Should().Be(storedSecret);
            persisted.ContainsKey("hasClientSecret").Should().BeFalse();
            db.ToolOAuthTokens.AsNoTracking().Count(t => t.McpOAuthConnectionId == connectionId).Should().Be(1);
        }

        // Review fix B1: a PUT that omits `config` gets the stored config (with the "enc:v1:..." secret) pre-filled
        // by the endpoint. That must keep the secret as-is and must not reset the tool's OAuth connections.
        [Fact]
        public async Task UpdateTenantTool_PutWithoutConfig_KeepsSecretAndConnections()
        {
            // Arrange
            const string storedSecret = "enc:v1:ZmFrZS1jaXBoZXJ0ZXh0";
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var created = (SuccessResult<TenantToolCreateResult>)await Fixture.GetMediator().Send(new CreateTenantToolCommand
            {
                Name = "Canva",
                Description = "Canva MCP",
                Type = CommonConsts.CommonToolTypeConsts.MCP,
                Config = McpOAuthConfig(clientSecret: null),
                IsActive = true
            }, TestContext.Current.CancellationToken);
            var toolId = created.Data.Id;
            var connectionId = created.Data.McpOAuthConnectionId.Value;

            var db = Fixture.GetDbContext();
            var entity = db.TenantTools.First(t => t.Id == toolId);
            var seeded = JObject.Parse(entity.Config);
            seeded["clientSecret"] = storedSecret;
            entity.Config = seeded.ToString(Formatting.None);
            db.ToolOAuthTokens.Add(new ToolOAuthToken { McpOAuthConnectionId = connectionId, AccessToken = "x", ConnectedOnUtc = System.DateTimeOffset.UtcNow, TokenObtainedAtUtc = System.DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");

            // Act — no "config" in the body at all
            using var data = new StringContent("{\"description\":\"Canva MCP renamed\"}", Encoding.UTF8, "application/json");
            var response = await Fixture.Client.PutAsync($"{UpdateUrl}/{toolId}", data, TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var persisted = db.TenantTools.AsNoTracking().First(t => t.Id == toolId);
            persisted.Description.Should().Be("Canva MCP renamed");
            JObject.Parse(persisted.Config).Value<string>("clientSecret").Should().Be(storedSecret);
            db.ToolOAuthTokens.AsNoTracking().Count(t => t.McpOAuthConnectionId == connectionId).Should().Be(1);
        }

        // Bug 7871: the edit form clears "Max Output Length" by sending an explicit null. That must reach the
        // database, even though the endpoint merges the request over the stored tool and skips nulls.
        [Fact]
        public async Task UpdateTenantTool_MaxToolOutputLengthExplicitNull_ClearsStoredCap()
        {
            // Arrange
            var toolId = await CreateToolWithMaxToolOutputLengthAsync(5000);

            // Act
            var response = await PutRawJsonAsync(toolId,
                "{\"name\":\"Test Name\",\"description\":\"Test Description\",\"type\":\"Test Type\",\"config\":\"Test Config\",\"isActive\":true,\"maxToolOutputLength\":null}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            JsonConvert.DeserializeObject<SuccessResult<TenantToolUpdateResult>>(body).Data.MaxToolOutputLength.Should().BeNull();
            GetStoredMaxToolOutputLength(toolId).Should().BeNull();
        }

        // Bug 7871: the tool card's enable/disable toggle sends a partial body with only isActive. An omitted
        // maxToolOutputLength must leave the stored cap alone.
        [Fact]
        public async Task UpdateTenantTool_MaxToolOutputLengthOmitted_KeepsStoredCap()
        {
            // Arrange
            var toolId = await CreateToolWithMaxToolOutputLengthAsync(5000);

            // Act
            var response = await PutRawJsonAsync(toolId, "{\"isActive\":false}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var stored = Fixture.GetDbContext().TenantTools.AsNoTracking().First(t => t.Id == toolId);
            stored.MaxToolOutputLength.Should().Be(5000);
            stored.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateTenantTool_MaxToolOutputLengthNumber_UpdatesStoredCap()
        {
            // Arrange
            var toolId = await CreateToolWithMaxToolOutputLengthAsync(5000);

            // Act
            var response = await PutRawJsonAsync(toolId, "{\"maxToolOutputLength\":1234}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            GetStoredMaxToolOutputLength(toolId).Should().Be(1234);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task UpdateTenantTool_MaxToolOutputLengthNotPositive_ResultInvalid(int value)
        {
            // Arrange
            var toolId = await CreateToolWithMaxToolOutputLengthAsync(5000);

            // Act
            var response = await PutRawJsonAsync(toolId, $"{{\"maxToolOutputLength\":{value}}}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            JsonConvert.DeserializeObject<InvalidResult>(body).Errors
                .Should().Contain(m => m.Code == ErrorConst.MustBePositive && m.Property == nameof(UpdateTenantToolCommand.MaxToolOutputLength));
            GetStoredMaxToolOutputLength(toolId).Should().Be(5000);
        }

        private async Task<int> CreateToolWithMaxToolOutputLengthAsync(int maxToolOutputLength)
        {
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var command = GetCreateCommandTenantTool();
            command.MaxToolOutputLength = maxToolOutputLength;
            var created = (SuccessResult<TenantToolCreateResult>)await Fixture.GetMediator().Send(command, TestContext.Current.CancellationToken);
            GetStoredMaxToolOutputLength(created.Data.Id).Should().Be(maxToolOutputLength);
            return created.Data.Id;
        }

        private async Task<HttpResponseMessage> PutRawJsonAsync(int toolId, string json)
        {
            ClearHeader();
            Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");
            using var data = new StringContent(json, Encoding.UTF8, "application/json");
            return await Fixture.Client.PutAsync($"{UpdateUrl}/{toolId}", data, TestContext.Current.CancellationToken);
        }

        private int? GetStoredMaxToolOutputLength(int toolId) =>
            Fixture.GetDbContext().TenantTools.AsNoTracking().First(t => t.Id == toolId).MaxToolOutputLength;

        private static string McpOAuthConfig(string clientSecret) => new JObject
        {
            ["id"] = "canva",
            ["name"] = "Canva",
            ["transportType"] = "Http",
            ["endpoint"] = "https://api.canva.com/connect/v1/mcp",
            ["authType"] = "OAuth2",
            ["oAuthScope"] = "PerUser",
            ["clientId"] = "canva-client",
            ["clientSecret"] = clientSecret
        }.ToString(Formatting.None);

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

        private static UpdateTenantToolCommand GetUpdateCommandTenantTool(int id)
        {
            return new UpdateTenantToolCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                Type = "Test Type 2",
                Config = "Test Config 2",
                IsActive = false
            };
        }

        private static UpdateTenantToolCommand GetUpdateCommandNotExistingTenantTool()
        {
            return new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            };
        }
    }
}

