using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetChatClassificationTopicByAgentId
{
    /// <summary>
    /// Fetches a single Chatbot Topic's current data (AC #4 of US 6809) by looking it up inside the
    /// current <see cref="AMAIS.WKFLO.Common.Models.CommonConsts.CommonToolTypeConsts.ChatClassification"/>
    /// tool config linked to <see cref="AgentId"/>, without requiring the caller to fetch/parse the whole
    /// tool config themselves. Consumed by AMAIS_DXP to resolve a Topic's current display name for a
    /// Touchpoint condition.
    /// </summary>
    public class GetChatClassificationTopicByAgentIdQuery : IWkfloRequest<Result>
    {
        public int AgentId { get; set; }
        public Guid TopicId { get; set; }
    }
}
