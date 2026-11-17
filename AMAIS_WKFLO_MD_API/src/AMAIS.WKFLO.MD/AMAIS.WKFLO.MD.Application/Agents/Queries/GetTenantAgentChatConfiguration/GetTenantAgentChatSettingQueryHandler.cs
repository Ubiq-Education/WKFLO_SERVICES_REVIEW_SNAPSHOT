using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentChatConfiguration
{
    public class GetTenantAgentChatSettingQueryHandler : IRequestHandler<GetTenantAgentChatSettingQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public GetTenantAgentChatSettingQueryHandler(
            IMasterDataDbContext db,
            IWkfloMapper mapper,
            TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetTenantAgentChatSettingQuery query, CancellationToken cancellationToken)
        {
            // First, get the TenantAgent by the provided ID
            var tenantAgent = await _db.TenantAgents
                .Include(ta => ta.PublishedTenantAgent)
                .FirstOrDefaultAsync(ta => ta.Id == query.AgentId && !ta.IsDeleted, cancellationToken);

            if (tenantAgent == null)
            {
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            // Check if the PublishedTenantAgent exists
            if (tenantAgent.PublishedTenantAgent == null)
            {
                return new SuccessResult<AgentChatSettingResult>(new AgentChatSettingResult()
                {
                    AgentId = query.AgentId,
                    ChatWidth = AgentChatSettingDefaults.ChatWidth,
                    PrimaryColor = AgentChatSettingDefaults.PrimaryColor,
                    BubbleStyle = AgentChatSettingDefaults.BubbleStyle,
                    FloatingPosition = AgentChatSettingDefaults.FloatingPosition,
                    LauncherStyle = AgentChatSettingDefaults.LauncherStyle,
                    IsWidget = query.IsWidget,
                    PredefinedPrompts = [],
                    SecurityConfiguration = new AgentSecurityConfiguration()
                    {
                        Type = "none"
                    }
                });
            }

            var publishedAgentId = tenantAgent.PublishedTenantAgent.Id;

            var storedConfiguration = await _db.AgentChatSettings
                .AsNoTracking()
                .Include(c => c.Agent)
                .ThenInclude(a => a.Tenant)
                .Include(c => c.PredefinedPrompts)
                .Where(c => c.AgentId == publishedAgentId &&
                        c.IsWidget == query.IsWidget &&
                        !c.IsDeleted &&
                        c.Agent.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (storedConfiguration != null)
            {
                var result = _mapper.Map<AgentChatSettingResult>(storedConfiguration);
                return new SuccessResult<AgentChatSettingResult>(result);
            }

            return new SuccessResult<AgentChatSettingResult>(new AgentChatSettingResult()
            {
                AgentId = query.AgentId,
                ChatWidth = AgentChatSettingDefaults.ChatWidth,
                PrimaryColor = AgentChatSettingDefaults.PrimaryColor,
                BubbleStyle = AgentChatSettingDefaults.BubbleStyle,
                FloatingPosition = AgentChatSettingDefaults.FloatingPosition,
                LauncherStyle = AgentChatSettingDefaults.LauncherStyle,
                IsWidget = query.IsWidget,
                PredefinedPrompts = [],
                SecurityConfiguration = new AgentSecurityConfiguration()
                {
                    Type = "none"
                }
            });
        }
    }

}
