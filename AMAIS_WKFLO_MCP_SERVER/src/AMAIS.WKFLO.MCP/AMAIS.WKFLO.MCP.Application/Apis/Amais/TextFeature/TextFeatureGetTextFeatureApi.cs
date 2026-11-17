using AMAIS.WKFLO.MCP.Application.Apis.Amais.TextFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.TextFeature
{
    public partial class TextFeatureApi
    {
        public async Task<GetTextFeatureResult> GetTextFeatureAsync(int id)
        {
            var path = $"/api/v1/features/text/{id}";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Get);

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new GetTextFeatureResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling GetTextFeatureAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GetTextFeatureResult>(content);
        }
    }
}
