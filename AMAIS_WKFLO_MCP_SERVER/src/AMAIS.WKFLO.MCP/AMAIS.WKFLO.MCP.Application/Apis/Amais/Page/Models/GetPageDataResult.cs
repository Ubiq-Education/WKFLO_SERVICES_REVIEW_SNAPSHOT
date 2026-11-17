using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class GetPageDataResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public GetPageData Data { get; set; }
    }

    [DataContract]
    public class GetPageData
    {
        [DataMember(Name = CommonPageMetadataConsts.Alias, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.Alias)]
        public string Alias { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.PageName)]
        public string PageName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageTags, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.PageTags)]
        public string[] PageTags { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageDescription, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.PageDescription)]
        public string PageDescription { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.MenuName, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.MenuName)]
        public string MenuName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.MetaTitle, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.MetaTitle)]
        public string MetaTitle { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.MetadaDescription, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.MetadaDescription)]
        public string MetadaDescription { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.OGMetaTitle, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.OGMetaTitle)]
        public string OGMetaTitle { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.OGMetaDescription, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.OGMetaDescription)]
        public string OGMetaDescription { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageFeatures, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.PageFeatures)]
        public IEnumerable<GetFeatureDataResult> PageFeatures { get; set; }
    }

    [DataContract]
    public class GetFeatureDataResult
    {
        [DataMember(Name = CommonMetadataConsts.Id, EmitDefaultValue = false)]
        [JsonProperty(CommonMetadataConsts.Id)]
        public int Id { get; set; }

        [DataMember(Name = "featureType", EmitDefaultValue = false)]
        [JsonProperty("featureType")]
        public string FeatureType { get; set; }

        [DataMember(Name = "contentType", EmitDefaultValue = false)]
        [JsonProperty("contentType")]
        public string ContentType { get; set; }

        [DataMember(Name = "content", EmitDefaultValue = false)]
        [JsonProperty("content")]
        public string Content { get; set; }
    }
}
