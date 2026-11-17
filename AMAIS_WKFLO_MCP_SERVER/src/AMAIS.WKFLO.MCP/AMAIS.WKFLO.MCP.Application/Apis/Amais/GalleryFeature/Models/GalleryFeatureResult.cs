using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models
{
    [DataContract]
    public class GalleryFeatureResult
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
    }
}
