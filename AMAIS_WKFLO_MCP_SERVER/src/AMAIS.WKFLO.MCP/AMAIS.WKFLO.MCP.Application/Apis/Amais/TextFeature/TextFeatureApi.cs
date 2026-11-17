using AMAIS.WKFLO.Common.ApiClient.Client;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.TextFeature
{
    public partial class TextFeatureApi : ApiBase
    {
        public ApiClient ApiClient { get; set; }

        public TextFeatureApi(string basePath, string _timeZoneId) : base(_timeZoneId)
        {
            this.ApiClient = new ApiClient(basePath);
        }

        public TextFeatureApi(HttpClient client, string _timeZoneId) : base(_timeZoneId)
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
    }
}
