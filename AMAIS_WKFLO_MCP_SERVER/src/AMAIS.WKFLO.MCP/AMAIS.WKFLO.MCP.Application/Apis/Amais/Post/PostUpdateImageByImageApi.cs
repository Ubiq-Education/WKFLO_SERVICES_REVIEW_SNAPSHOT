using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post
{
    public partial class PostApi : ApiBase
    {
        public async Task<UpdatePostImageResult> UpdatePostImageByImage(
            string postAliasToUpdate,
            string newImageName,
            byte[] newImageBytes,
            string newImageMimeType
            )
        {
            var path = "/api/v1/post/update/image";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Put, postBody: new
            {
                postAliasToUpdate,
                newImageName,
                newImageBytes,
                newImageMimeType
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new UpdatePostImageResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling UpdatePostImageByImage: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<UpdatePostImageResult>(content);
        }
    }
}
