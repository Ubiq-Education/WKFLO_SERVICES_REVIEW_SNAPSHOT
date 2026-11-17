using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class UpdatePageResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public UpdatePageData Data { get; set; }
    }

    [DataContract]
    public class UpdatePageData
    {
        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }

        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url)]
        public string Url { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.PageName)]
        public string PageName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.MenuName, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.MenuName)]
        public string MenuName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.Alias, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.Alias)]
        public string Alias { get; set; }

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
    }

}
