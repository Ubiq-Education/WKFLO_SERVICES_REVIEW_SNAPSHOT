using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi
    {
        public async Task<GetGalleryFeatureResult> GetGalleryFeatureAsync(int id)
        {
            var path = $"/api/v1/features/gallery/{id}";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Get);

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new GetGalleryFeatureResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling GetGalleryFeatureAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GetGalleryFeatureResult>(content);
        }
    }
}
