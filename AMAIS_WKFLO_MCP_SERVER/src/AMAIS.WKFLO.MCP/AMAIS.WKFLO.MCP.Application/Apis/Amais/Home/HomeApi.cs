using AMAIS.WKFLO.Common.ApiClient.Client;
using AMAIS.WKFLO.Common.ApiClient.Exceptions;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Home.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Home
{
    public class HomeApi : ApiBase
    {
        public ApiClient ApiClient { get; set; }

        public HomeApi(string basePath, string _timeZoneId) : base(_timeZoneId)
        {
            this.ApiClient = new ApiClient(basePath);
        }

        public HomeApi(HttpClient client, string _timeZoneId) : base(_timeZoneId)
        {
            this.ApiClient = new ApiClient(client);
        }

        public void SetBasePath(String basePath)
        {
            this.ApiClient.BasePath = basePath;
        }

        public String GetBasePath()
        {
            return this.ApiClient.BasePath;
        }

        public async Task<WelcomeMessageResult> WelcomeMessageAsync()
        {
            var path = "/api/v1/home/welcome";

            IFlurlResponse response = await ApiClient.CallApiAsync(path, HttpMethod.Post);

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                throw new ApiException((int)response.StatusCode, "Error calling WelcomeMessageAsync: " + error, response.ResponseMessage);
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<WelcomeMessageResult>(content);
        }
    }
}
