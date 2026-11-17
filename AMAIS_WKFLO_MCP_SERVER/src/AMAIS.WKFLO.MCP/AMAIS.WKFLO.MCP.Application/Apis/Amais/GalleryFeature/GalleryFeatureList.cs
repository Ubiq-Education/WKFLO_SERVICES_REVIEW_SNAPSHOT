using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi
    {
        public async Task<ListGalleryFeaturesResult> ListGalleryFeaturesAsync(List<string> searchTerms)
        {
            var path = "/api/v1/galleryfeature/list";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                searchTerms
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ListGalleryFeaturesResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ListGalleryFeaturesAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ListGalleryFeaturesResult>(content);
        }
    }
}
