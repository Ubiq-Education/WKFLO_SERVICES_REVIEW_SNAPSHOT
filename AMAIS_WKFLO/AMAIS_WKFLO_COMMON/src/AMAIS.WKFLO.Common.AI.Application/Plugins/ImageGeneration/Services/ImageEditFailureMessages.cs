namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    /// <summary>Customer-safe wording per <see cref="ImageEditFailureKind"/> (US 7564). No provider details.</summary>
    public static class ImageEditFailureMessages
    {
        public static string Friendly(ImageEditFailureKind kind) => kind switch
        {
            ImageEditFailureKind.SafetyBlocked => "The image service's safety filters blocked this request, so the image wasn't changed. Try rewording the instruction or using a different image.",
            ImageEditFailureKind.Overloaded => "The image service is very busy right now. Please try again shortly.",
            ImageEditFailureKind.RateLimited => "Too many image requests were made in a short time. Please wait a minute and try again.",
            ImageEditFailureKind.QuotaExceeded => "The image service has reached its usage limit. Please contact Support.",
            ImageEditFailureKind.InvalidRequest => "The image service couldn't process this image or instruction. Try a different image or a simpler instruction.",
            ImageEditFailureKind.NotConfigured => "The image editing tool isn't set up correctly. Please contact Support.",
            _ => "Something went wrong while editing the image. Please try again. If it keeps happening, please contact Support."
        };

        /// <summary>Short description for the tool-execution update (shown in the admin test chat).</summary>
        public static string Short(ImageEditFailureKind kind) => kind switch
        {
            ImageEditFailureKind.SafetyBlocked => "blocked by the image service's safety filters.",
            ImageEditFailureKind.Overloaded => "the image service is overloaded.",
            ImageEditFailureKind.RateLimited => "rate limit reached.",
            ImageEditFailureKind.QuotaExceeded => "usage limit reached.",
            ImageEditFailureKind.InvalidRequest => "the image or instruction couldn't be processed.",
            ImageEditFailureKind.NotConfigured => "the tool isn't set up correctly.",
            _ => "unexpected error."
        };
    }
}
