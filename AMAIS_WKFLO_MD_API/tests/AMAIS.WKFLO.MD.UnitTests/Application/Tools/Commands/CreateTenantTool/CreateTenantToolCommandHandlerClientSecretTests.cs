using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.CreateTenantTool
{
    /// <summary>US 7849: a client secret posted on create is encrypted before it is saved.</summary>
    public class CreateTenantToolCommandHandlerClientSecretTests
    {
        private static readonly IMcpTokenProtector Protector = BuildProtector();

        private static IMcpTokenProtector BuildProtector()
        {
            var configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[AesMcpTokenProtector.ConfigurationKey]).Returns(Convert.ToBase64String(new byte[32]));
            return new AesMcpTokenProtector(configuration.Object);
        }

        private static (CreateTenantToolCommandHandler Handler, List<TenantTool> Added, Mock<IMasterDataDbContext> Db) Build()
        {
            var added = new List<TenantTool>();
            var tenantTools = new List<TenantTool>().BuildMockDbSet();
            tenantTools.Setup(x => x.AddAsync(It.IsAny<TenantTool>(), It.IsAny<CancellationToken>()))
                .Callback((TenantTool t, CancellationToken _) => added.Add(t));

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { new() { Id = 1, Name = "Test" } }.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            var handler = new CreateTenantToolCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(), new TenantContext { TenantName = "Test" },
                Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" }), Protector);

            return (handler, added, db);
        }

        private static CreateTenantToolCommand Command(string clientId, string clientSecret) => new()
        {
            Name = "Canva",
            Description = "Canva MCP",
            Type = CommonToolTypeConsts.MCP,
            IsActive = true,
            Config = new JObject
            {
                ["id"] = "canva",
                ["name"] = "Canva",
                ["transportType"] = "Http",
                ["endpoint"] = "https://api.canva.com/connect/v1/mcp",
                ["authType"] = "OAuth2",
                ["clientId"] = clientId,
                ["clientSecret"] = clientSecret,
                ["hasClientSecret"] = false
            }.ToString(Newtonsoft.Json.Formatting.None)
        };

        [Fact]
        public async Task Handle_NewSecret_IsEncryptedBeforeSave()
        {
            var (handler, added, _) = Build();

            var result = await handler.Handle(Command("canva-client", "s3cret"), TestContext.Current.CancellationToken);

            Assert.IsType<SuccessResult<TenantToolCreateResult>>(result);
            var saved = Assert.Single(added);
            Assert.DoesNotContain("s3cret", saved.Config);
            Assert.DoesNotContain("hasClientSecret", saved.Config);
            var stored = JObject.Parse(saved.Config).Value<string>("clientSecret");
            Assert.Equal("s3cret", McpClientSecretHelper.Resolve(Protector, stored));
            Assert.NotNull(saved.McpOAuthConnectionId);
        }

        [Fact]
        public async Task Handle_McpConfigNotJson_ReturnsInvalidAndSavesNothing()
        {
            var (handler, added, _) = Build();
            var command = Command("canva-client", "s3cret");
            command.Config = "{ \"clientSecret\": \"pl41n\", broken";

            var result = await handler.Handle(command, TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpConfigInvalid);
            Assert.Empty(added);
        }

        [Fact]
        public async Task Handle_SecretWithoutClientId_ReturnsInvalidAndSavesNothing()
        {
            var (handler, added, db) = Build();

            var result = await handler.Handle(Command("", "s3cret"), TestContext.Current.CancellationToken);

            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            Assert.Empty(added);
            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
