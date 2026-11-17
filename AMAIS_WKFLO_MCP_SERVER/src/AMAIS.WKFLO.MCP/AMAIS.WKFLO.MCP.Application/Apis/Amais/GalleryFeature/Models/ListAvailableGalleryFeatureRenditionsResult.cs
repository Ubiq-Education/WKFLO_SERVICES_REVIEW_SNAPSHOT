using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models
{
    [DataContract]
    public class ListAvailableGalleryFeatureRenditionsResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public ListAvailableGalleryFeatureImagesData Data { get; set; }
    }

    [DataContract]
    public class ListAvailableGalleryFeatureImagesData
    {
        [DataMember(Name = CommonGalleryFeatureMetadataConsts.Renditions, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.Renditions)]
        public List<AvailableGalleryFeatureRenditionsData> Renditions { get; set; }

        /// <summary>
        /// Requested rendition IDs that the CMS did not return (only set for rendition ID lookups).
        /// </summary>
        [DataMember(Name = CommonGalleryFeatureMetadataConsts.NotFoundRenditionIds, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.NotFoundRenditionIds, NullValueHandling = NullValueHandling.Ignore)]
        public List<int> NotFoundRenditionIds { get; set; }

        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message, NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }
    }

    [DataContract]
    public class AvailableGalleryFeatureRenditionsData
    {
        [DataMember(Name = CommonGalleryFeatureMetadataConsts.RenditionId, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonGalleryFeatureMetadataConsts.RenditionId)]
        public int RenditionId { get; set; }

        [DataMember(Name = CommonMetadataConsts.Name, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Name)]
        public string Name { get; set; }

        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url)]
        public string Url { get; set; }

        [DataMember(Name = CommonMetadataConsts.LastUpdatedDate, EmitDefaultValue = false)]
        [JsonProperty(CommonMetadataConsts.LastUpdatedDate)]
        public DateTimeOffset LastUpdatedDate { get; set; }
    }

}
