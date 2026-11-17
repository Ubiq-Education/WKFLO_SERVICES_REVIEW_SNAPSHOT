using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.Models.Config;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration
{
    internal static class ImageEditServiceFactory
    {
        public static IImageEditService Create(ImageGenerationToolConfig config) =>
            config.Provider?.ToLowerInvariant() switch
            {
                "azure" => new AzureGptImageEditService(config),
                "google" => new GoogleImageEditService(config),
                _ => throw new NotSupportedException($"Image edit provider '{config.Provider}' is not supported. Use 'azure' or 'google'.")
            };
    }
}
