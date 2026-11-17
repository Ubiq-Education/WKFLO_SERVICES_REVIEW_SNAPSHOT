using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post
{
    public partial class PostApi : ApiBase
    {
        public async Task<UpdatePostResult> UpdatePostAsync(
            string postAliasToUpdate,
            string newPostName,
            string newMenuName,
            string newMetaTitle,
            string newMetaDescription,
            string newOGMetaTitle,
            string newOGMetaDescription
            )
        {
            var path = "/api/v1/post/update";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Put, postBody: new
            {
                postAliasToUpdate,
                newPostName,
                newMenuName,
                newMetaTitle,
                newMetaDescription,
                newOGMetaTitle,
                newOGMetaDescription
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new UpdatePostResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling UpdatePostAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<UpdatePostResult>(content);
        }
    }
}
