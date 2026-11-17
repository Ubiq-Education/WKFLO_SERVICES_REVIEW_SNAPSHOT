using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post
{
    public partial class PostApi : ApiBase
    {
        public async Task<ListAvailablePostImagesResult> ListAvailablePostImagesAsync(string searchTerms)
        {
            var path = "/api/v1/post/available/image/list";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                searchTerms
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ListAvailablePostImagesResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ListAvailablePostImagesNamesAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ListAvailablePostImagesResult>(content);
        }
    }
}
