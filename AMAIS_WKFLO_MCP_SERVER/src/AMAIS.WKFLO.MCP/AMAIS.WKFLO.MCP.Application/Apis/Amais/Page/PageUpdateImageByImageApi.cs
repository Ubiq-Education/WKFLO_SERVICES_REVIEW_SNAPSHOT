using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page
{
    public partial class PageApi
    {
        public async Task<UpdatePageImageResult> UpdatePageImageByImage(
            string pageAliasToUpdate,
            string newImageName,
            byte[] newImageBytes,
            string newImageMimeType
            )
        {
            var path = "/api/v1/page/update/image";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Put, postBody: new
            {
                pageAliasToUpdate,
                newImageName,
                newImageBytes,
                newImageMimeType
            });

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new UpdatePageImageResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling UpdatePageImageByImage: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<UpdatePageImageResult>(content);
        }
    }
}
