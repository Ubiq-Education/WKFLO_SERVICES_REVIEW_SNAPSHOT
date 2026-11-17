using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Home.Models
{
    [DataContract]
    public class WelcomeMessageResult
    {
        [DataMember(Name = "resultType", EmitDefaultValue = false)]
        [JsonProperty(PropertyName = "resultType")]
        public int ResultType { get; set; }

        [DataMember(Name = "data", EmitDefaultValue = false)]
        [JsonProperty(PropertyName = "data")]
        public WelcomeMessageData Data { get; set; }
    }

    public class WelcomeMessageData
    {
        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }
    }
}
