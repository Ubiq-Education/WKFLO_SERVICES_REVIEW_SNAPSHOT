using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    /// <summary>
    /// Covers the topic-version bump that <see cref="UpdateTenantToolCommandHandler"/> applies for
    /// ChatClassification tools — this is the "generic tool-update command path" (US 5911, Phase 2) that
    /// saves <c>Tool.Config</c> and is the one place responsible for incrementing
    /// <see cref="ChatClassificationToolConfig.Version"/> whenever the topic configuration actually changes.
    /// </summary>
    public class UpdateTenantToolCommandHandlerChatClassificationVersioningTests
    {
        private static readonly Guid TopicId = Guid.NewGuid();
        private static readonly IOptions<McpOAuthSettings> Settings = Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" });

        private static (UpdateTenantToolCommandHandler handler, TenantTool tool) BuildHandler(string initialConfigJson)
        {
            var mapper = WkfloMapperExtensions.CreateMapper();

            var tenantTool = new TenantTool
            {
                Id = 1,
                Name = "Classifier",
                Description = "desc",
                Type = CommonToolTypeConsts.ChatClassification,
                Config = initialConfigJson,
                IsActive = true
            };

            var tenantTools = new List<TenantTool> { tenantTool }.BuildMockDbSet();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));

            return (new UpdateTenantToolCommandHandler(db.Object, mapper, Settings, Mock.Of<IMcpTokenProtector>()), tenantTool);
        }

        private static string BuildConfigJson(int? version, params (Guid id, string name)[] topics)
        {
            var config = new ChatClassificationToolConfig
            {
                Topics = topics.Select(t => new ClassificationTopic { Id = t.id, Name = t.name, IsActive = true }).ToList()
            };
            if (version.HasValue)
                config.Version = version.Value;

            return JsonConvert.SerializeObject(config);
        }

        private static ChatClassificationToolConfig ExtractConfig(Result result) =>
            JsonConvert.DeserializeObject<ChatClassificationToolConfig>(
                ((SuccessResult<TenantToolUpdateResult>)result).Data.Config);

        [Fact]
        public async Task Handle_TopicsChanged_IncrementsVersion()
        {
            var (handler, _) = BuildHandler(BuildConfigJson(version: 3, (TopicId, "Billing")));

            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Classifier",
                Description = "desc",
                Type = CommonToolTypeConsts.ChatClassification,
                Config = BuildConfigJson(version: null, (TopicId, "Billing Renamed"))
            }, TestContext.Current.CancellationToken);

            Assert.Equal(4, ExtractConfig(result).Version);
        }

        [Fact]
        public async Task Handle_TopicsUnchanged_PreservesVersion()
        {
            var (handler, _) = BuildHandler(BuildConfigJson(version: 3, (TopicId, "Billing")));

            // Same topics resent (e.g. only Name/Description/IsActive of the tool itself changed) — version
            // must not bump even though Config is technically re-saved with the exact same topic content.
            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Classifier Renamed",
                Description = "new desc",
                Type = CommonToolTypeConsts.ChatClassification,
                Config = BuildConfigJson(version: null, (TopicId, "Billing"))
            }, TestContext.Current.CancellationToken);

            Assert.Equal(3, ExtractConfig(result).Version);
        }

        [Fact]
        public async Task Handle_AddingATopic_IncrementsVersion()
        {
            var (handler, _) = BuildHandler(BuildConfigJson(version: 1, (TopicId, "Billing")));

            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Classifier",
                Description = "desc",
                Type = CommonToolTypeConsts.ChatClassification,
                Config = BuildConfigJson(version: null, (TopicId, "Billing"), (Guid.NewGuid(), "Support"))
            }, TestContext.Current.CancellationToken);

            Assert.Equal(2, ExtractConfig(result).Version);
        }

        [Fact]
        public async Task Handle_PreviousConfigHadNoVersionField_TreatsItAsOneBeforeIncrementing()
        {
            // Simulates a tool configured before the Version field existed — no "version" property at all.
            const string legacyConfigJson = """{"topics":[{"id":"11111111-1111-1111-1111-111111111111","name":"Billing"}]}""";
            var (handler, _) = BuildHandler(legacyConfigJson);

            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Classifier",
                Description = "desc",
                Type = CommonToolTypeConsts.ChatClassification,
                Config = BuildConfigJson(version: null, (Guid.NewGuid(), "Support"))
            }, TestContext.Current.CancellationToken);

            Assert.Equal(2, ExtractConfig(result).Version);
        }

        [Fact]
        public async Task Handle_NonChatClassificationTool_DoesNotTouchConfig()
        {
            var mapper = WkfloMapperExtensions.CreateMapper();
            var tenantTool = new TenantTool
            {
                Id = 1,
                Name = "Http Tool",
                Description = "desc",
                Type = "HTTP",
                Config = "{\"url\":\"https://example.com\"}",
                IsActive = true
            };
            var tenantTools = new List<TenantTool> { tenantTool }.BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(tenantTools.Object);
            db.Setup(x => x.SaveChangesAsync(default)).Returns(Task.FromResult(1));
            var handler = new UpdateTenantToolCommandHandler(db.Object, mapper, Settings, Mock.Of<IMcpTokenProtector>());

            const string newConfig = "{\"url\":\"https://example.com/v2\"}";
            var result = await handler.Handle(new UpdateTenantToolCommand
            {
                Id = 1,
                Name = "Http Tool",
                Description = "desc",
                Type = "HTTP",
                Config = newConfig
            }, TestContext.Current.CancellationToken);

            Assert.Equal(newConfig, ((SuccessResult<TenantToolUpdateResult>)result).Data.Config);
        }
    }
}
