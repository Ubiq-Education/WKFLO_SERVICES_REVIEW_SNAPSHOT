using Flurl.Http;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Downloads the image to edit. A failure here is about our stored image (e.g. an expired link), not the provider.</summary>
    internal static class ImageEditSourceDownloader
    {
        public static async Task<byte[]> DownloadAsync(string imageUrl, CancellationToken cancellationToken)
        {
            try
            {
                return await imageUrl.GetBytesAsync(cancellationToken: cancellationToken);
            }
            // Only a 4xx means our stored link is gone or expired. Timeouts, 5xx and network errors propagate to the classifier
            // (Overloaded / Other, logged at the right level); a caller cancellation (possibly Flurl-wrapped) is never wrapped.
            catch (FlurlHttpException ex) when (ex is not FlurlHttpTimeoutException && ex.StatusCode is >= 400 and < 500 && !cancellationToken.IsCancellationRequested)
            {
                throw new ImageEditInputException("The image couldn't be loaded. Please upload it again.", ex);
            }
        }
    }
}
