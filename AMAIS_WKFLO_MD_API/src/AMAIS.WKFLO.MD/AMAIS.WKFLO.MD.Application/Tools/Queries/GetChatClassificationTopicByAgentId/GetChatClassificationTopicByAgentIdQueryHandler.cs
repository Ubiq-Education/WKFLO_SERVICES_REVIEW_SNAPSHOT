using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetChatClassificationTopicByAgentId
{
    public class GetChatClassificationTopicByAgentIdQueryHandler : IRequestHandler<GetChatClassificationTopicByAgentIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public GetChatClassificationTopicByAgentIdQueryHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(GetChatClassificationTopicByAgentIdQuery query, CancellationToken cancellationToken)
        {
            // Exactly one Chat_Classification tool is expected per agent (the tenant tool config UI enforces
            // this), so the first match linked to the requested agent (and tenant) is the tool of record.
            var tool = await _db.TenantTools
                .AsNoTracking()
                .Where(t =>
                    t.Type == CommonToolTypeConsts.ChatClassification &&
                    t.Tenant.Name == _tenantContext.TenantName &&
                    t.AgentTools.Any(at => at.AgentId == query.AgentId && at.Agent.Tenant.Name == _tenantContext.TenantName))
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(TenantTool), ErrorConst.ChatClassificationToolNotFound);

            ChatClassificationToolConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(tool.Config) ?? new ChatClassificationToolConfig();
            }
            catch (JsonException)
            {
                // Malformed/legacy config JSON — there is no topic to resolve.
                return new NotFoundResult(nameof(ClassificationTopic), ErrorConst.ChatClassificationTopicNotFound);
            }

            var topic = config.Topics?.FirstOrDefault(t => t.Id == query.TopicId);

            if (topic == null)
                return new NotFoundResult(nameof(ClassificationTopic), ErrorConst.ChatClassificationTopicNotFound);

            var result = new ChatClassificationTopicGetResult
            {
                Id = topic.Id,
                Name = topic.Name,
                Description = topic.Description,
                IsActive = topic.IsActive,
                HandoffMessage = topic.HandoffMessage,
                AgentId = query.AgentId,
                ToolId = tool.Id,
                Version = ChatClassificationConfigHelper.ResolveTopicVersion(config),
                ToolLastUpdatedOn = tool.LastUpdatedOn ?? tool.CreatedOn
            };

            return new SuccessResult<ChatClassificationTopicGetResult>(result);
        }
    }
}
