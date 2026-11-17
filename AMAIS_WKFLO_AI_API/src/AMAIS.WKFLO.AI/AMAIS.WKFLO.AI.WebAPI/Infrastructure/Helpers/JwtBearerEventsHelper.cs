using AMAIS.WKFLO.Common.Contexts;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.Helpers
{
    public static class JwtBearerEventsHelper
    {
        /// <summary>
        /// Extracts the 'aud' claim from a validated JWT and populates
        /// <see cref="ChatContext.BasePath"/> so it is available throughout
        /// the request — including Dapr background tasks where
        /// IHttpContextAccessor is unavailable.
        /// </summary>
        public static void ApplyAudienceToBasePath(JsonWebToken jwt, IServiceProvider services)
        {
            if (!jwt.TryGetClaim("aud", out var audClaim) || string.IsNullOrWhiteSpace(audClaim.Value))
                return;

            var chatContext = services.GetRequiredService<ChatContext>();
            chatContext.BasePath = audClaim.Value;
        }
    }
}
