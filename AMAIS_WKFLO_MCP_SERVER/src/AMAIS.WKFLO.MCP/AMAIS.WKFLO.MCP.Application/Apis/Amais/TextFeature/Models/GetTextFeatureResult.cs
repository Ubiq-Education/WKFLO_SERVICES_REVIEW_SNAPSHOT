using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.TextFeature.Models
{
    [DataContract]
    public class GetTextFeatureResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public GetTextFeatureData Data { get; set; }
    }

    [DataContract]
    public class GetTextFeatureData
    {
        [DataMember(Name = CommonMetadataConsts.Id, EmitDefaultValue = false)]
        [JsonProperty(CommonMetadataConsts.Id)]
        public int Id { get; set; }

        [DataMember(Name = CommonFeatureMetadataConsts.Status, EmitDefaultValue = false)]
        [JsonProperty(CommonFeatureMetadataConsts.Status)]
        public string Status { get; set; }

        [DataMember(Name = CommonFeatureMetadataConsts.TextSaved, EmitDefaultValue = false)]
        [JsonProperty(CommonFeatureMetadataConsts.TextSaved)]
        public string TextSaved { get; set; }

        [DataMember(Name = CommonFeatureMetadataConsts.TextPublished, EmitDefaultValue = false)]
        [JsonProperty(CommonFeatureMetadataConsts.TextPublished)]
        public string TextPublished { get; set; }

        [DataMember(Name = CommonFeatureMetadataConsts.PublishedDate, EmitDefaultValue = false)]
        [JsonProperty(CommonFeatureMetadataConsts.PublishedDate)]
        public DateTime? PublishedDate { get; set; }
    }
}
