using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post
{
    public partial class PostApi : ApiBase
    {
        public async Task<ClonePostResult> ClonePostAsync(string postAliasToClone, string newPostName, bool clearSeoAndSocialMetadata = false, bool versionUnsharedFeatures = false)
        {
            var path = "/api/v1/post/clone";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                postAliasToClone,
                newPostName,
                clearSeoAndSocialMetadata,
                versionUnsharedFeatures
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ClonePostResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ClonePostAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ClonePostResult>(content);
        }
    }
}
