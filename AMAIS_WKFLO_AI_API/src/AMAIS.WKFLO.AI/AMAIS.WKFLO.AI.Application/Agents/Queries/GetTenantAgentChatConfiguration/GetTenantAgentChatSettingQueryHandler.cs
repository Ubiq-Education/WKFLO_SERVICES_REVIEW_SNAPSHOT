using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Contexts;
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

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetTenantAgentChatConfiguration
{
    public class GetTenantAgentChatSettingQueryHandler : IRequestHandler<GetTenantAgentChatSettingQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public GetTenantAgentChatSettingQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantAgentChatSettingQuery query, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents
                .Include(m => m.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == query.Id && !m.IsDeleted && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            var settingResult = await CreateTenantAgentChatSettingResult(agent, cancellationToken);
            return new SuccessResult<TenantAgentChatSettingResult>(settingResult);
        }

        private async Task<TenantAgentChatSettingResult> CreateTenantAgentChatSettingResult(TenantAgent tenantAgent, CancellationToken cancellationToken)
        {
            var settingResult = new TenantAgentChatSettingResult();

            if (tenantAgent?.AgentTools != null && tenantAgent.AgentTools.Any(m => m.Tool.Type == CommonToolTypeConsts.IntegrationAmais))
            {
                var toolResult = _mapper.Map<ToolResult>(tenantAgent.AgentTools
                    .Where(m => m.Tool.Type == CommonToolTypeConsts.IntegrationAmais)
                    .Select(m => m.Tool)
                    .First());

                settingResult.IsVoiceChatEnabled = toolResult.AmaisIntegrationToolConfiguration?.IsVoiceEnabled == true;
                settingResult.AreSkillsEnabled = toolResult.AmaisIntegrationToolConfiguration?.AreSkillsEnabled == true;
                settingResult.VoiceName = toolResult.AmaisIntegrationToolConfiguration?.VoiceName;
                settingResult.VoiceLanguageCode = toolResult.AmaisIntegrationToolConfiguration?.VoiceLanguageCode;
            }

            settingResult.IsMaisyAgent = await AgentTagHelper.IsMaisyAgentAsync(_db, tenantAgent?.Id, cancellationToken);
            settingResult.CanSelectFeature = settingResult.IsMaisyAgent;
            settingResult.DisplaySettings = await LoadDisplaySettingsAsync(cancellationToken);

            return settingResult;
        }

        /// <summary>US 7974: the current tenant's date/time display settings, or their defaults.</summary>
        private async Task<TenantDisplaySettingsResult> LoadDisplaySettingsAsync(CancellationToken cancellationToken)
        {
            var setting = await _db.TenantSettings
                .AsNoTracking()
                .Where(s => s.Tenant.Name == _tenantContext.TenantName && !s.IsDeleted)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            return TenantDisplaySettingsResult.Create(setting);
        }
    }
}
