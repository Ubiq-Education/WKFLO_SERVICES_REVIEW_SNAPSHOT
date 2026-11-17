using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models
{
    [DataContract]
    public class GalleryImageResult
    {
        [DataMember(Name = CommonGalleryFeatureMetadataConsts.RenditionId, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.RenditionId)]
        public int RenditionId { get; set; }

        [DataMember(Name = CommonGalleryFeatureMetadataConsts.GalleryId, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.GalleryId)]
        public int GalleryId { get; set; }

        [DataMember(Name = CommonGalleryFeatureMetadataConsts.Height, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.Height)]
        public int? Height { get; set; }

        [DataMember(Name = CommonGalleryFeatureMetadataConsts.Width, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.Width)]
        public int? Width { get; set; }

        [DataMember(Name = CommonGalleryFeatureMetadataConsts.PresetID, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.PresetID)]
        public int? PresetID { get; set; }

        /// <summary>
        /// Rendition URL. Only sent by CMS versions that support it; older versions leave it null.
        /// </summary>
        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url, NullValueHandling = NullValueHandling.Ignore)]
        public string Url { get; set; }

        /// <summary>
        /// Rendition name. Only sent by CMS versions that support it; older versions leave it null.
        /// </summary>
        [DataMember(Name = CommonMetadataConsts.Name, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Name, NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }
}
