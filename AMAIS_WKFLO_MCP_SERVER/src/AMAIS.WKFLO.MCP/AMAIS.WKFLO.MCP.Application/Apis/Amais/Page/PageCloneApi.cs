using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page
{
    public partial class PageApi
    {
        public async Task<ClonePageResult> ClonePageAsync(string pageAliasToClone, string newPageName, bool clearSeoAndSocialMetadata = false, bool versionUnsharedFeatures = false)
        {
            var path = "/api/v1/page/clone";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                pageAliasToClone,
                newPageName,
                clearSeoAndSocialMetadata,
                versionUnsharedFeatures
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ClonePageResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ClonePageAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ClonePageResult>(content);
        }
    }
}
