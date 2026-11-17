using Google.GenAI.Types;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    public interface IGoogleContentGenerationClient
    {
        Task<GenerateContentResponse> GenerateContentAsync(string model, Content contents, GenerateContentConfig config, CancellationToken cancellationToken);
    }
}
