using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    internal class RunImageAnalyzerChatData
    {
        public Kernel Kernel { get; set; }
        public RunnableAgent Agent { get; set; }
        public string Message { get; set; }
        public List<FileAttachment> Files { get; set; }
        public string SessionId { get; set; }
    }
}
