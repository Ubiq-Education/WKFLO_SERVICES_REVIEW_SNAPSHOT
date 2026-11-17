using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class CreatePageResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public CreatePageData Data { get; set; }
    }

    [DataContract]
    public class CreatePageData
    {
        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }

        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url)]
        public string Url { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.NewPageCreatedName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.NewPageCreatedName)]
        public string NewPageCreatedName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.NewPageCreatedAlias, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.NewPageCreatedAlias)]
        public string NewPageCreatedAlias { get; set; }
    }

}
