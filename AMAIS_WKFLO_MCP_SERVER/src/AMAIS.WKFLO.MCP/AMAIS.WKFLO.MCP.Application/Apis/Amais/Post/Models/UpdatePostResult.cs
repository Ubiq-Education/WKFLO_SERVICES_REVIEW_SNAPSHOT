using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models
{
    [DataContract]
    public class UpdatePostResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public UpdatePostData Data { get; set; }
    }

    [DataContract]
    public class UpdatePostData
    {
        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }

        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url)]
        public string Url { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.PostName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPostMetadataConsts.PostName)]
        public string PostName { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.MenuName, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.MenuName)]
        public string MenuName { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.Alias, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.Alias)]
        public string Alias { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.MetaTitle, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.MetaTitle)]
        public string MetaTitle { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.MetadaDescription, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.MetadaDescription)]
        public string MetadaDescription { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.OGMetaTitle, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.OGMetaTitle)]
        public string OGMetaTitle { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.OGMetaDescription, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.OGMetaDescription)]
        public string OGMetaDescription { get; set; }
    }

}
