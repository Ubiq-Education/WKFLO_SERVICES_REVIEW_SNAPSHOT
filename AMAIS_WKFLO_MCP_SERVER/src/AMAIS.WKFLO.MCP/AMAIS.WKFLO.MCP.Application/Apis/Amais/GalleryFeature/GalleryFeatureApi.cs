using AMAIS.WKFLO.Common.ApiClient.Client;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi : ApiBase
    {
        public ApiClient ApiClient { get; set; }

        public GalleryFeatureApi(string basePath, string _timeZoneId) : base(_timeZoneId)
        {
            this.ApiClient = new ApiClient(basePath);
        }

        public GalleryFeatureApi(HttpClient client, string _timeZoneId) : base(_timeZoneId)
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
