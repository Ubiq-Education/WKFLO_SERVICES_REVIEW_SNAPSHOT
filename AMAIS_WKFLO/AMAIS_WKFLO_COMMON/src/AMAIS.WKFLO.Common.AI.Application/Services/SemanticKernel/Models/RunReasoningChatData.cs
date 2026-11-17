using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    public class RunReasoningChatData
    {
        public Kernel Kernel { get; set; }
        public RunnableAgent Agent { get; set; }
        public string Prompt { get; set; }
        public string Message { get; set; }
        public string ImageAnalysis { get; set; }
        public string SessionId { get; set; }
        public ChatHistoryAgentThread ChatHistoryAgentThread { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.RootIsWidget"/> — forwarded into this reasoning sub-step's
        /// icon resolution.
        /// </summary>
        public bool? RootIsWidget { get; set; }
    }
}
