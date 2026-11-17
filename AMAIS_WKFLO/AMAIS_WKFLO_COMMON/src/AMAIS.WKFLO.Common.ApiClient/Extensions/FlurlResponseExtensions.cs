using Flurl.Http;

namespace AMAIS.WKFLO.Common.ApiClient.Extensions
{
    public static class FlurlResponseExtensions
    {
        public static bool IsWorthRetrying(this IFlurlResponse response)
        {
            switch (response.StatusCode)
            {
                case 408:
                case 500:
                case 502:
                case 503:
                case 504:
                    return true;
                default:
                    return false;
            }
        }
    }
}
