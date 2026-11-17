using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI
{
    public record ImageEditResult(byte[] ImageBytes, string MimeType, string Extension, AITokenUsage TokenUsage);

    public interface IImageEditService
    {
        Task<ImageEditResult> EditImageAsync(string imageUrl, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default);
        Task<ImageEditResult> EditImageAsync(byte[] imageBytes, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default);
    }
}
