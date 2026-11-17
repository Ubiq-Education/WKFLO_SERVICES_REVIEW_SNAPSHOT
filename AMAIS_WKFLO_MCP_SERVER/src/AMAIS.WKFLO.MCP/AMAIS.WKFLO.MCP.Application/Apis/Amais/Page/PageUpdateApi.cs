using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page
{
    public partial class PageApi
    {
        public async Task<UpdatePageResult> UpdatePageAsync(
            string pageAliasToUpdate,
            string newPageName,
            string newMenuName,
            string newMetaTitle,
            string newMetaDescription,
            string newOGMetaTitle,
            string newOGMetaDescription
            )
        {
            var path = "/api/v1/page/update";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Put, postBody: new
            {
                pageAliasToUpdate,
                newPageName,
                newMenuName,
                newMetaTitle,
                newMetaDescription,
                newOGMetaTitle,
                newOGMetaDescription
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new UpdatePageResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling UpdatePageAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<UpdatePageResult>(content);
        }
    }
}
