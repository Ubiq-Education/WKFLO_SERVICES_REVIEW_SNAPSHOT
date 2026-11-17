using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.Extensions.Configuration;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.UpdateMarketplaceTool
{
    /// <summary>US 7849: marketplace tools follow the same client-secret contract as tenant tools.</summary>
    public class UpdateMarketplaceToolCommandHandlerClientSecretTests
    {
        private static readonly IMcpTokenProtector Protector = BuildProtector();

        private static IMcpTokenProtector BuildProtector()
        {
            var configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[AesMcpTokenProtector.ConfigurationKey]).Returns(Convert.ToBase64String(new byte[32]));
            return new AesMcpTokenProtector(configuration.Object);
        }

        private static string Config(string clientSecret = null) => new JObject
        {
            ["transportType"] = "Http",
            ["endpoint"] = "https://api.canva.com/connect/v1/mcp",
            ["authType"] = "OAuth2",
            ["clientId"] = "canva-client",
            ["clientSecret"] = clientSecret
        }.ToString(Newtonsoft.Json.Formatting.None);

        private static (UpdateMarketplaceToolCommandHandler Handler, MarketplaceTool Tool) Build(string storedConfig)
        {
            var tool = new MarketplaceTool { Id = 7001, Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = storedConfig };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.MarketplaceTools).Returns(new List<MarketplaceTool> { tool }.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            return (new UpdateMarketplaceToolCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(), Protector), tool);
        }

        private static UpdateMarketplaceToolCommand Command(string config) =>
            new() { Id = 7001, Name = "Canva", Description = "d", Type = CommonToolTypeConsts.MCP, Config = config };

        [Fact]
        public async Task Handle_BlankSecret_KeepsStoredSecret()
        {
            var storedSecret = McpClientSecretHelper.Protect(Protector, "s3cret");
            var (handler, tool) = Build(Config(storedSecret));

            var result = await handler.Handle(Command(Config()), TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<MarketplaceToolUpdateResult>>(result);
            Assert.Equal(storedSecret, JObject.Parse(tool.Config).Value<string>("clientSecret"));
        }

        [Fact]
        public async Task Handle_McpConfigNotJson_ReturnsInvalid()
        {
            var (handler, _) = Build(Config());

            var result = await handler.Handle(Command("not json \"clientSecret\": \"pl41n\""), TestContext.Current.CancellationToken);

            Assert.Contains(Assert.IsType<InvalidResult>(result).Errors, e => e.Code == ErrorConst.ToolMcpConfigInvalid);
        }

        [Fact]
        public async Task Handle_TypeChangedFromMcp_StripsCredentials()
        {
            var storedSecret = McpClientSecretHelper.Protect(Protector, "s3cret");
            var (handler, tool) = Build(Config(storedSecret));
            var command = Command(Config(storedSecret));
            command.Type = "HTTP";

            var result = await handler.Handle(command, TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<MarketplaceToolUpdateResult>>(result);
            Assert.DoesNotContain(storedSecret, tool.Config);
            Assert.DoesNotContain("canva-client", tool.Config);
            Assert.DoesNotContain(storedSecret, success.Data.Config);
        }

        [Fact]
        public async Task CreateMarketplaceTool_McpConfigNotJson_ReturnsInvalid()
        {
            var db = new Mock<IMasterDataDbContext>();
            var handler = new AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool.CreateMarketplaceToolCommandHandler(
                db.Object, WkfloMapperExtensions.CreateMapper(), new AMAIS.WKFLO.Common.Contexts.TenantContext { TenantName = "T" }, Protector);

            var result = await handler.Handle(new AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool.CreateMarketplaceToolCommand
            {
                Name = "Canva",
                Description = "d",
                Type = CommonToolTypeConsts.MCP,
                Config = "not json"
            }, TestContext.Current.CancellationToken);

            Assert.Contains(Assert.IsType<InvalidResult>(result).Errors, e => e.Code == ErrorConst.ToolMcpConfigInvalid);
            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_NewSecret_IsEncrypted()
        {
            var (handler, tool) = Build(Config());

            await handler.Handle(Command(Config("new-secret")), TestContext.Current.CancellationToken);

            Assert.DoesNotContain("new-secret", tool.Config);
            Assert.Equal("new-secret", McpClientSecretHelper.Resolve(Protector, JObject.Parse(tool.Config).Value<string>("clientSecret")));
        }
    }
}
