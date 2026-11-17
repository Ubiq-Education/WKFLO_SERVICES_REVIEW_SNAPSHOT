using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.Models.Results.Token
{
    public class TokenResponseResult
    {
        public string Token { get; set; }

        public string Message { get; set; }

        [JsonPropertyName("site_id")]
        public string SiteId { get; set; }

        public Dictionary<string, string> Claims { get; set; }
    }
}
