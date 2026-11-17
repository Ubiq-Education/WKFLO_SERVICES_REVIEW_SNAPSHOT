using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSettingShareable
{
    public class UpdateAgentChatSettingShareableCommandHandler : IRequestHandler<UpdateAgentChatSettingShareableCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateAgentChatSettingShareableCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateAgentChatSettingShareableCommand command, CancellationToken cancellationToken)
        {
            // First, get the TenantAgent by the provided ID
            var tenantAgent = await _db.TenantAgents
                .Include(ta => ta.PublishedTenantAgent)
                .FirstOrDefaultAsync(ta => ta.Id == command.Id && !ta.IsDeleted, cancellationToken);

            // Check if the PublishedTenantAgent exists
            if (tenantAgent.PublishedTenantAgent == null)
            {
                return new InvalidResult(nameof(tenantAgent.PublishedTenantAgent), ErrorConst.AgentShareNotPublished);
            }

            var publishedAgentId = tenantAgent.PublishedTenantAgent.Id;

            // Look for existing AgentChatSetting using the PublishedTenantAgent ID
            var agentChatSetting = await _db.AgentChatSettings
                .FirstOrDefaultAsync(x => x.AgentId == publishedAgentId && x.IsWidget == command.IsWidget && !x.IsDeleted, cancellationToken);

            if (agentChatSetting == null)
            {
                // Create new AgentChatSetting
                agentChatSetting = _mapper.Map<AgentChatSetting>(command);
                agentChatSetting.AgentId = publishedAgentId;
                // This command doesn't carry ShowDescription/ChainOfThought, so Mapster leaves
                // them at the C# bool default (false) — set the real business defaults explicitly
                // for newly-created settings only; existing settings keep whatever is stored.
                agentChatSetting.ShowDescription = AgentChatSettingDefaults.ShowDescription;
                agentChatSetting.ChainOfThought = AgentChatSettingDefaults.ChainOfThought;
                await _db.AgentChatSettings.AddAsync(agentChatSetting, cancellationToken);
            }

            // Update IsShareable property
            agentChatSetting.IsShareable = command.IsShareable;
            agentChatSetting.IsWidget = command.IsWidget;
            agentChatSetting.BubbleStyle ??= AgentChatSettingDefaults.BubbleStyle;
            agentChatSetting.PrimaryColor ??= AgentChatSettingDefaults.PrimaryColor;
            agentChatSetting.FloatingPosition ??= AgentChatSettingDefaults.FloatingPosition;
            agentChatSetting.LauncherStyle ??= AgentChatSettingDefaults.LauncherStyle;
            agentChatSetting.ChatWidth ??= AgentChatSettingDefaults.ChatWidth;

            // If setting to shareable and no ShareId exists, generate one
            if (string.IsNullOrEmpty(agentChatSetting.ShareId))
            {
                agentChatSetting.ShareId = Guid.NewGuid().ToString();
            }

            await _db.SaveChangesAsync(cancellationToken);

            var result = _mapper.Map<AgentChatSettingUpdateResult>(agentChatSetting);
            return new SuccessResult<AgentChatSettingUpdateResult>(result);
        }
    }
}