using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Services;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommandHandler : IRequestHandler<UpdateTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly McpOAuthSettings _mcpOAuthSettings;
        private readonly IMcpTokenProtector _protector;

        public UpdateTenantToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, IOptions<McpOAuthSettings> mcpOAuthSettings, IMcpTokenProtector protector)
        {
            _db = db;
            _mapper = mapper;
            _mcpOAuthSettings = mcpOAuthSettings.Value;
            _protector = protector;
        }

        public async ValueTask<Result> Handle(UpdateTenantToolCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = await _db.TenantTools
                .Include(m => m.AgentTools)
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantTool == null)
                return new NotFoundResult(nameof(TenantTool), ErrorConst.ToolNotFound);

            var previousType = tenantTool.Type;
            var previousConfig = tenantTool.Config;

            _mapper.Map(command, tenantTool);

            // US 7849: the UI never receives the stored OAuth client secret, so it can't resend it — merge it
            // back in (or replace/clear/drop it) and encrypt any new one before anything else touches Config.
            var secretMerge = McpClientSecretHelper.MergeOnWrite(
                _protector, tenantTool.Type, tenantTool.Config, previousType == CommonToolTypeConsts.MCP ? previousConfig : null);
            if (secretMerge.IsInvalidConfig)
                return new InvalidResult(nameof(TenantTool.Config), ErrorConst.ToolMcpConfigInvalid);
            if (secretMerge.IsClientSecretWithoutClientId)
                return new InvalidResult(nameof(TenantTool.Config), ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            tenantTool.Config = secretMerge.Config;

            // A tool that stops being MCP keeps no MCP credentials (the PUT endpoint pre-fills the old config,
            // and nothing would manage or redact them as MCP any more) and no OAuth connection.
            var leftMcp = previousType == CommonToolTypeConsts.MCP && tenantTool.Type != CommonToolTypeConsts.MCP;
            if (leftMcp)
                tenantTool.Config = McpClientSecretHelper.StripCredentials(tenantTool.Config);

            // Only ChatClassification tools carry a topic-version counter. Bump it here — the one place
            // that saves this tool's Config — whenever the topic configuration actually changed, so
            // "Chatbot Message Topic" DXP events sent under the new config can be told apart from ones
            // sent under the old one. Use the pre-update Type: a tool is not expected to change Type on
            // update, but the persisted value is the more trustworthy source of "was this a ChatClassification
            // tool" than blindly trusting whatever the caller resent.
            if (previousType == CommonToolTypeConsts.ChatClassification)
                tenantTool.Config = BumpTopicVersionIfChanged(previousConfig, tenantTool.Config);

            McpOAuthConnectionIdProvisioner.EnsureProvisioned(tenantTool, _mcpOAuthSettings);

            // WI 7575: null = unchanged. Like every other tool field, it reaches the published copy only when the
            // tool is published (draft/publish model).
            if (command.PermissionsEnabled.HasValue)
            {
                tenantTool.PermissionsEnabled = command.PermissionsEnabled.Value;
            }

            if ((secretMerge.CredentialsChanged || leftMcp) && tenantTool.McpOAuthConnectionId.HasValue)
                await InvalidateOAuthConnectionAsync(tenantTool.McpOAuthConnectionId.Value, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantToolUpdateResult>(_mapper.Map<TenantToolUpdateResult>(tenantTool));
        }

        /// <summary>
        /// The tool's OAuth client id, endpoint or secret changed (or it stopped being an MCP tool), so every token
        /// issued under the old settings — the shared row and every user's per-user row — is deleted and everyone
        /// reconnects with the new ones. In-flight consent flows started under the old settings are dropped too.
        /// A callback that already loaded its flow before this runs is caught by
        /// <c>McpOAuthConsentService.CompleteAsync</c>, which re-checks the flow's client id and endpoint against
        /// the tool's current config before saving a token; a secret-only change relies on this flow deletion
        /// alone, leaving only the tiny window between that callback's flow lookup and this save. The published
        /// copy shares this connection id, so it is reset as well. Removals are saved together with the tool update.
        /// </summary>
        private async Task InvalidateOAuthConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
        {
            var tokens = await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId)
                .ToListAsync(cancellationToken);
            if (tokens.Count > 0)
                _db.ToolOAuthTokens.RemoveRange(tokens);

            var pendingFlows = await _db.McpOAuthPendingFlows
                .Where(m => m.ConnectionId == connectionId)
                .ToListAsync(cancellationToken);
            if (pendingFlows.Count > 0)
                _db.McpOAuthPendingFlows.RemoveRange(pendingFlows);
        }

        /// <summary>
        /// Re-serializes <paramref name="newConfigJson"/> with <see cref="ChatClassificationToolConfig.Version"/>
        /// incremented from whatever <paramref name="previousConfigJson"/> carried, but only when the active
        /// topic set actually differs — renaming/toggling/adding/removing a topic all count, since all of them
        /// change what a classification made under this version means. Any other field update (tool Name,
        /// Description, IsActive) with the exact same topics leaves the version untouched.
        /// </summary>
        private static string BumpTopicVersionIfChanged(string previousConfigJson, string newConfigJson)
        {
            ChatClassificationToolConfig newConfig;
            try
            {
                newConfig = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(newConfigJson) ?? new ChatClassificationToolConfig();
            }
            catch (JsonException)
            {
                // Not parseable as ChatClassification config — nothing to version, leave the raw value as-is.
                return newConfigJson;
            }

            ChatClassificationToolConfig previousConfig = null;
            if (!string.IsNullOrWhiteSpace(previousConfigJson))
            {
                try
                {
                    previousConfig = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(previousConfigJson);
                }
                catch (JsonException)
                {
                    // Malformed previous config (e.g. legacy data) — treat as unknown history, handled below.
                }
            }

            var previousVersion = previousConfig?.Version ?? 1;
            newConfig.Version = TopicsChanged(previousConfig?.Topics, newConfig.Topics) ? previousVersion + 1 : previousVersion;

            return JsonConvert.SerializeObject(newConfig);
        }

        private static bool TopicsChanged(List<ClassificationTopic> previousTopics, List<ClassificationTopic> newTopics)
        {
            var previousCanonical = (previousTopics ?? []).Select(CanonicalizeTopic).OrderBy(x => x, StringComparer.Ordinal);
            var newCanonical = (newTopics ?? []).Select(CanonicalizeTopic).OrderBy(x => x, StringComparer.Ordinal);

            return !previousCanonical.SequenceEqual(newCanonical);
        }

        private static string CanonicalizeTopic(ClassificationTopic topic) =>
            $"{topic.Id:N}|{topic.Name}|{topic.Description}|{topic.IsActive}|{topic.HandoffMessage}";
    }
}
