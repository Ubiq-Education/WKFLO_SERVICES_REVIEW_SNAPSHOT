using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page
{
    public partial class PageApi
    {
        public async Task<GetPageDataResult> GetPageDataAsync(string pageAlias)
        {
            var path = $"/api/v1/page/get-by-alias/{pageAlias}";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Get);

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new GetPageDataResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling GetPageDataAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GetPageDataResult>(content);
        }
    }
}
