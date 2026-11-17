using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models
{
    [DataContract]
    public class ListGalleryFeaturesResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public ListGalleryFeatureData Data { get; set; }
    }

    [DataContract]
    public class ListGalleryFeatureData
    {
        [DataMember(Name = CommonGalleryFeatureMetadataConsts.Galleries, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.Galleries)]
        public List<GalleryFeatureResult> Galleries { get; set; }
    }
}
