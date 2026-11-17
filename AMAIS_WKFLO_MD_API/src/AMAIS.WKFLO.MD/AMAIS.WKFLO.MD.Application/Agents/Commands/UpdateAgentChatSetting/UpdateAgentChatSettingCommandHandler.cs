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

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSetting
{
    public class UpdateAgentChatSettingCommandHandler : IRequestHandler<UpdateAgentChatSettingCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<UpdateAgentChatSettingCommandHandler> _logger;

        public UpdateAgentChatSettingCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext, ILogger<UpdateAgentChatSettingCommandHandler> logger)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(UpdateAgentChatSettingCommand command, CancellationToken cancellationToken)
        {
            // Verify agent exists and belongs to tenant
            var tenantAgent = await _db.TenantAgents
                .Include(ta => ta.PublishedTenantAgent)
                .Where(a => a.Id == command.Id &&
                           a.Tenant.Name == _tenantContext.TenantName &&
                           !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantAgent == null)
            {
                return new InvalidResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            // Check if the PublishedTenantAgent exists
            if (tenantAgent.PublishedTenantAgent == null)
            {
                return new InvalidResult(nameof(tenantAgent.PublishedTenantAgent), ErrorConst.AgentShareNotPublished);
            }

            var publishedAgentId = tenantAgent.PublishedTenantAgent.Id;

            // Get existing AgentChatSetting
            var existingSetting = await _db.AgentChatSettings
                .Where(acs => acs.AgentId == publishedAgentId && acs.IsWidget == command.IsWidget)
                .Include(acs => acs.PredefinedPrompts)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingSetting == null)
            {
                // Create new AgentChatSetting 
                existingSetting = _mapper.Map<AgentChatSetting>(command);
                existingSetting.AgentId = publishedAgentId;
                existingSetting.Agent = tenantAgent.PublishedTenantAgent;
                existingSetting.BubbleStyle ??= AgentChatSettingDefaults.BubbleStyle;
                existingSetting.FloatingPosition ??= AgentChatSettingDefaults.FloatingPosition;
                existingSetting.LauncherStyle ??= AgentChatSettingDefaults.LauncherStyle;
                existingSetting.ChatWidth ??= AgentChatSettingDefaults.ChatWidth;
                existingSetting.PrimaryColor ??= AgentChatSettingDefaults.PrimaryColor;
                existingSetting.ShowDescription = command.ShowDescription ?? AgentChatSettingDefaults.ShowDescription;
                existingSetting.SecurityConfiguration = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(existingSetting.SecurityConfiguration, _logger);
                if (command.PredefinedPrompts != null)
                {
                    existingSetting.PredefinedPrompts = command.PredefinedPrompts
                        .Select(prompt => new PredefinedPrompt
                        {
                            Title = prompt.Title,
                            Prompt = prompt.Prompt,
                            AgentChatSetting = existingSetting
                        })
                        .ToList();
                }
                if (existingSetting.PredefinedPrompts != null)
                {
                    foreach (var prompt in existingSetting.PredefinedPrompts)
                    {
                        prompt.AgentChatSetting = existingSetting;
                    }
                }
                await _db.AgentChatSettings.AddAsync(existingSetting, cancellationToken);
                SyncAgentIconUrl(tenantAgent, existingSetting.ChatbotIconPath);
                await _db.SaveChangesAsync(cancellationToken);
                return new SuccessResult<AgentChatSettingUpdateResult>(_mapper.Map<AgentChatSettingUpdateResult>(existingSetting));
            }

            // Update the existing setting — for fields the caller omitted (null), keep whatever
            // is already stored instead of letting Mapster null them out; only truly-new settings
            // fall back to the hardcoded default.
            command.BubbleStyle ??= existingSetting.BubbleStyle ?? AgentChatSettingDefaults.BubbleStyle;
            command.FloatingPosition ??= existingSetting.FloatingPosition ?? AgentChatSettingDefaults.FloatingPosition;
            command.LauncherStyle ??= existingSetting.LauncherStyle ?? AgentChatSettingDefaults.LauncherStyle;
            command.ChatWidth ??= existingSetting.ChatWidth ?? AgentChatSettingDefaults.ChatWidth;
            command.PrimaryColor ??= existingSetting.PrimaryColor ?? AgentChatSettingDefaults.PrimaryColor;
            // ShowDescription is nullable on the command so an omitted field preserves the
            // already-stored value here, instead of Mapster silently mapping it to false.
            command.ShowDescription ??= existingSetting.ShowDescription;
            _mapper.Map(command, existingSetting);
            existingSetting.SecurityConfiguration = AgentSecurityConfigurationHelper.ApplyDefaultJwtDomains(existingSetting.SecurityConfiguration, _logger);
            if (command.PredefinedPrompts != null)
            {
                var existingPrompts = await _db.PredefinedPrompts
                    .Where(prompt => prompt.AgentChatSettingId == existingSetting.Id)
                    .ToListAsync(cancellationToken);

                existingSetting.PredefinedPrompts = existingPrompts;

                var existingByTitle = existingPrompts
                    .Where(prompt => !string.IsNullOrWhiteSpace(prompt.Title))
                    .ToDictionary(prompt => prompt.Title.Trim(), StringComparer.OrdinalIgnoreCase);

                var incomingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var promptModel in command.PredefinedPrompts)
                {
                    var promptTitle = promptModel.Title?.Trim();
                    if (string.IsNullOrWhiteSpace(promptTitle) || !incomingTitles.Add(promptTitle))
                    {
                        continue;
                    }

                    if (existingByTitle.TryGetValue(promptTitle, out var existingPrompt))
                    {
                        existingPrompt.Title = promptModel.Title;
                        existingPrompt.Prompt = promptModel.Prompt;
                        continue;
                    }

                    existingSetting.PredefinedPrompts.Add(new PredefinedPrompt
                    {
                        Title = promptModel.Title,
                        Prompt = promptModel.Prompt,
                        AgentChatSetting = existingSetting
                    });
                }

                var promptsToRemove = existingPrompts
                    .Where(prompt => !string.IsNullOrWhiteSpace(prompt.Title) &&
                        !incomingTitles.Contains(prompt.Title.Trim()))
                    .ToList();

                if (promptsToRemove.Count > 0)
                {
                    _db.PredefinedPrompts.RemoveRange(promptsToRemove);
                }
            }
            SyncAgentIconUrl(tenantAgent, existingSetting.ChatbotIconPath);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AgentChatSettingUpdateResult>(_mapper.Map<AgentChatSettingUpdateResult>(existingSetting));
        }

        /// <summary>
        /// Write-through so Agent.IconUrl (what TenantAgent.List/Get expose) reflects the chat icon for
        /// agents created directly in WKFLO. Only a non-empty icon is written: clearing/omitting the chat
        /// icon must not wipe an icon the agent already has (e.g. one inherited from a marketplace template).
        /// </summary>
        private static void SyncAgentIconUrl(TenantAgent tenantAgent, string chatbotIconPath)
        {
            if (string.IsNullOrWhiteSpace(chatbotIconPath))
                return;

            tenantAgent.IconUrl = chatbotIconPath;
            if (tenantAgent.PublishedTenantAgent != null)
                tenantAgent.PublishedTenantAgent.IconUrl = chatbotIconPath;
        }
    }
}