using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.AI.Providers;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    public class RunVoiceOptions
    {
        public string Message { get; set; }
        public string SessionId { get; set; }
        public RunnableAgent Agent { get; set; }
        public List<string> AgentTagNames { get; set; }
        public AIBaseProvider Provider { get; set; }
        public string BranchId { get; set; }
        public string VoiceName { get; set; }
        public string VoiceLangCode { get; set; }
        public bool ChainOfThought { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.RootIsWidget"/> — same meaning, for the voice invocation path.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.IsMaisyAgent"/> — same meaning, for the voice invocation path.
        /// </summary>
        public bool IsMaisyAgent { get; set; }
    }
}
