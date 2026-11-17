namespace AMAIS.WKFLO.Common.Models.Results
{
    /// <summary>
    /// Result of an <see cref="AMAIS.WKFLO.Common.Interfaces.IImageStorageService"/> upload.
    /// </summary>
    public class ImageStorageResult
    {
        /// <summary>
        /// The relative path the image was stored at within the container, as supplied by the caller.
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// The absolute URL the image is reachable at: {STATIC_WEBSITE_BASE_URL env var}/{RelativePath}.
        /// Null if that env var isn't configured in the current environment.
        /// </summary>
        public string AbsoluteUrl { get; set; }
    }
}
