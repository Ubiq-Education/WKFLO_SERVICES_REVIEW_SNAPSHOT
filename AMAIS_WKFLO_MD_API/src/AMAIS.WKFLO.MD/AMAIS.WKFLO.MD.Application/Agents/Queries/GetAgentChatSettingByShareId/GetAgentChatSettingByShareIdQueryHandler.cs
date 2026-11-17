using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetAgentChatSettingByShareId
{
    public class GetAgentChatSettingByShareIdQueryHandler : IRequestHandler<GetAgentChatSettingByShareIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetAgentChatSettingByShareIdQueryHandler(
            IMasterDataDbContext db,
            IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetAgentChatSettingByShareIdQuery query, CancellationToken cancellationToken)
        {
            var storedConfiguration = await _db.AgentChatSettings
                .AsNoTracking()
                .Include(c => c.Agent)
                .ThenInclude(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Include(c => c.Agent)
                .ThenInclude(c => c.Tenant)
                .Include(c => c.PredefinedPrompts)
                .Where(c => c.ShareId == query.ShareId &&
                           !c.IsDeleted &&
                           !c.Agent.IsDeleted &&
                           c.IsShareable)
                .FirstOrDefaultAsync(cancellationToken);

            if (storedConfiguration != null)
            {
                var result = _mapper.Map<AgentChatSettingResult>(storedConfiguration);
                TryLoadAmaisIntegrationToolSettings(result, storedConfiguration);
                result.IsMaisyAgent = await AgentTagHelper.IsMaisyAgentAsync(_db, storedConfiguration.Agent?.Id, cancellationToken);
                result.CanSelectFeature = result.IsMaisyAgent;
                return new SuccessResult<AgentChatSettingResult>(result);
            }

            return new NotFoundResult(nameof(query.ShareId), ErrorConst.AgentNotFound);
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
            }
        }
    }
}