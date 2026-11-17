using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi
    {
        public async Task<CreateGalleryFeatureResult> CreateGalleryFeatureAsync(string name, List<string> tags)
        {
            var path = "/api/v1/galleryfeature/create";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                name,
                tags
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new CreateGalleryFeatureResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling CreateGalleryFeatureAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<CreateGalleryFeatureResult>(content);
        }

    }
}
