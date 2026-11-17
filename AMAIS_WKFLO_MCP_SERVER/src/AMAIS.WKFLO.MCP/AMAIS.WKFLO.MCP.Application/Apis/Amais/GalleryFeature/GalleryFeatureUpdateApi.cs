using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi
    {
        public async Task<UpdateGalleryFeatureResult> UpdateGalleryFeatureAsync(
            int id,
            string name,
            List<string> tags,
            List<GalleryImageRequest> images,
            List<int> renditionIds,
            int imageSizeStrategy = 0,
            int? presetId = null,
            int? imageWidth = null,
            int? imageHeight = null
            )
        {
            var path = "/api/v1/galleryfeature/update";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Put, postBody: new
            {
                id,
                name,
                tags,
                images,
                renditionIds,
                imageSizeStrategy,
                presetId,
                imageWidth,
                imageHeight
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new UpdateGalleryFeatureResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling UpdateGalleryFeatureAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<UpdateGalleryFeatureResult>(content);
        }
    }
}
