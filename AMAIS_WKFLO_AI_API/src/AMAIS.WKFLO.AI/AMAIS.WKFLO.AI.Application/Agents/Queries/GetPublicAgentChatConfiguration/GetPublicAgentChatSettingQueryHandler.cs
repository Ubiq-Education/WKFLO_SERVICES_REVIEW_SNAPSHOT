using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools;
using AMAIS.WKFLO.Common.Models.Results;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatConfiguration
{
    public class GetPublicAgentChatSettingQueryHandler : IRequestHandler<GetPublicAgentChatSettingQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetPublicAgentChatSettingQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetPublicAgentChatSettingQuery query, CancellationToken cancellationToken)
        {
            var storedConfiguration = await _db.AgentChatSettings
                .AsNoTracking()
                .Include(c => c.Agent)
                .ThenInclude(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Include(c => c.PredefinedPrompts)
                .Where(c => c.ShareId == query.ShareId && !c.IsDeleted && !c.Agent.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (storedConfiguration != null)
            {
                var result = _mapper.Map<AgentChatSettingResult>(storedConfiguration);
                TryLoadAmaisIntegrationToolSettings(result, storedConfiguration);
                result.IsMaisyAgent = await AgentTagHelper.IsMaisyAgentAsync(_db, storedConfiguration.Agent?.Id, cancellationToken);
                result.CanSelectFeature = result.IsMaisyAgent;
                result.DisplaySettings = await LoadDisplaySettingsAsync(storedConfiguration.Agent?.TenantId, cancellationToken);
                return new SuccessResult<AgentChatSettingResult>(result);
            }

            return new NotFoundResult(nameof(query.ShareId), ErrorConst.AgentNotFound);
        }

        /// <summary>US 7974: the agent's tenant's date/time display settings, or their defaults.</summary>
        private async Task<TenantDisplaySettingsResult> LoadDisplaySettingsAsync(int? tenantId, CancellationToken cancellationToken)
        {
            var setting = tenantId is null ? null : await _db.TenantSettings
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId.Value && !s.IsDeleted)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            return TenantDisplaySettingsResult.Create(setting);
        }

        private void TryLoadAmaisIntegrationToolSettings(AgentChatSettingResult settingResult, AgentChatSetting storedConfiguration)
        {
            if (storedConfiguration.Agent?.AgentTools != null && storedConfiguration.Agent.AgentTools.Any(m => m.Tool.Type == CommonToolTypeConsts.IntegrationAmais))
            {
                var toolResult = _mapper.Map<ToolResult>(storedConfiguration.Agent.AgentTools
                    .Where(m => m.Tool.Type == CommonToolTypeConsts.IntegrationAmais)
                    .Select(m => m.Tool)
                    .First());

                settingResult.IsVoiceChatEnabled = toolResult.AmaisIntegrationToolConfiguration?.IsVoiceEnabled == true;
                settingResult.AreSkillsEnabled = toolResult.AmaisIntegrationToolConfiguration?.AreSkillsEnabled == true;
                settingResult.VoiceName = toolResult.AmaisIntegrationToolConfiguration?.VoiceName;
                settingResult.VoiceLanguageCode = toolResult.AmaisIntegrationToolConfiguration?.VoiceLanguageCode;
            }
        }
    }
}
