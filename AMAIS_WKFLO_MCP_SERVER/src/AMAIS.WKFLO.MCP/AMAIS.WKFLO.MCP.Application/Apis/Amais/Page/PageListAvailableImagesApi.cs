using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page
{
    public partial class PageApi
    {
        public async Task<ListAvailablePageImagesResult> ListAvailablePageImagesAsync(string searchTerms)
        {
            var path = "/api/v1/page/available/image/list";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post, postBody: new
            {
                searchTerms
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ListAvailablePageImagesResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ListAvailablePageImagesNamesAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ListAvailablePageImagesResult>(content);
        }
    }
}
