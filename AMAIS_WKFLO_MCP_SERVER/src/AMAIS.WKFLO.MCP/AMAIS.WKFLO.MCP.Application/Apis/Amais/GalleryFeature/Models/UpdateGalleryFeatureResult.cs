using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models
{
    [DataContract]
    public class UpdateGalleryFeatureResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public UpdateGalleryFeatureData Data { get; set; }
    }

    [DataContract]
    public class UpdateGalleryFeatureData
    {
        [DataMember(Name = CommonMetadataConsts.Id, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Id)]
        public int Id { get; set; }

        [DataMember(Name = CommonMetadataConsts.Name, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Name)]
        public string Name { get; set; }

        [DataMember(Name = CommonMetadataConsts.Tags, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Tags)]
        public string Tags { get; set; }

        [DataMember(Name = CommonMetadataConsts.LastUpdatedDate, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.LastUpdatedDate)]
        public DateTimeOffset LastUpdatedDate { get; set; }

        [DataMember(Name = CommonGalleryFeatureMetadataConsts.GalleryImages, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.GalleryImages)]
        public List<GalleryImageResult> GalleryImages { get; set; }

        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }
    }

}
