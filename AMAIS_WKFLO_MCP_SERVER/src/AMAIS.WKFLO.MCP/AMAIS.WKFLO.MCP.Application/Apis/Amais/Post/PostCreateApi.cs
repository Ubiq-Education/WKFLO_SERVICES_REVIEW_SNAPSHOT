using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post
{
    public partial class PostApi
    {
        public async Task<CreatePostResult> CreatePostAsync(string postName)
        {
            var path = "/api/v1/post/create";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new { postName });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new CreatePostResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling CreatePostAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<CreatePostResult>(content);
        }
    }
}
