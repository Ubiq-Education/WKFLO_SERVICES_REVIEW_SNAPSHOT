using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Common;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateAgentChatSetting
{
    public class CreateAgentChatSettingCommandHandler : IRequestHandler<CreateAgentChatSettingCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<CreateAgentChatSettingCommandHandler> _logger;

        public CreateAgentChatSettingCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext, ILogger<CreateAgentChatSettingCommandHandler> logger)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(CreateAgentChatSettingCommand command, CancellationToken cancellationToken)
        {
            // Verify tenant exists
            var tenant = await _db.Tenants
                .Where(t => t.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
            {
                return new InvalidResult(nameof(TenantContext.TenantName), ErrorConst.TenantInvalid)
                    .AddError(nameof(TenantContext.TenantName), ErrorConst.TenantMustExist);
            }

            // Verify agent exists and belongs to tenant
            var agent = await _db.TenantAgents
                .Include(a => a.PublishedTenantAgent)
                .Where(a => a.Id == command.Id &&
                           a.TenantId == tenant.Id &&
                           !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
            {
                return new InvalidResult(nameof(Agent), ErrorConst.AgentMustExist);
            }

            // Check if AgentChatSetting already exists for this agent
            var existingSetting = await _db.AgentChatSettings
                .Where(acs => acs.AgentId == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingSetting != null)
            {
                return new InvalidResult(nameof(AgentChatSetting), ErrorConst.AgentChatSettingAlreadyExists);
            }

            // Create new AgentChatSetting
            var agentChatSetting = _mapper.Map<AgentChatSetting>(command);
            agentChatSetting.AgentId = command.Id;
            agentChatSetting.Agent = agent;
            agentChatSetting.BubbleStyle ??= AgentChatSettingDefaults.BubbleStyle;
            agentChatSetting.FloatingPosition ??= AgentChatSettingDefaults.FloatingPosition;
            agentChatSetting.LauncherStyle ??= AgentChatSettingDefaults.LauncherStyle;
            agentChatSetting.ChatWidth ??= AgentChatSettingDefaults.ChatWidth;
            agentChatSetting.PrimaryColor ??= AgentChatSettingDefaults.PrimaryColor;
            agentChatSetting.ShowDescription = command.ShowDescription ?? AgentChatSettingDefaults.ShowDescription;
            agentChatSetting.SecurityConfiguration = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(agentChatSetting.SecurityConfiguration, _logger);
            if (command.PredefinedPrompts != null)
            {
                agentChatSetting.PredefinedPrompts = _mapper.Map<List<PredefinedPrompt>>(command.PredefinedPrompts);
                foreach (var prompt in agentChatSetting.PredefinedPrompts)
                {
                    prompt.AgentChatSetting = agentChatSetting;
                }
            }

            await _db.AgentChatSettings.AddAsync(agentChatSetting, cancellationToken);

            // Write-through so Agent.IconUrl (exposed by TenantAgent.List/Get) reflects the chat icon.
            // Only a non-empty icon is written; an empty one never clears an existing IconUrl.
            if (!string.IsNullOrWhiteSpace(agentChatSetting.ChatbotIconPath))
            {
                agent.IconUrl = agentChatSetting.ChatbotIconPath;
                if (agent.PublishedTenantAgent != null)
                    agent.PublishedTenantAgent.IconUrl = agentChatSetting.ChatbotIconPath;
            }
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AgentChatSettingCreateResult>(_mapper.Map<AgentChatSettingCreateResult>(agentChatSetting));
        }
    }
}