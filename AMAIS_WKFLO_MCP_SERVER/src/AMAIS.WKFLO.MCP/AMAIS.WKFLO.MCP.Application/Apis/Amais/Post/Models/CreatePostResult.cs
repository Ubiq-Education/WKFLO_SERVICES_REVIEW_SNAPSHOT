using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models
{
    [DataContract]
    public class CreatePostResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public CreatePostData Data { get; set; }
    }

    [DataContract]
    public class CreatePostData
    {
        [DataMember(Name = CommonMetadataConsts.Message, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Message)]
        public string Message { get; set; }

        [DataMember(Name = CommonMetadataConsts.Url, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Url)]
        public string Url { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.NewPostCreatedName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPostMetadataConsts.NewPostCreatedName)]
        public string NewPostCreatedName { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.NewPostCreatedAlias, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.NewPostCreatedAlias)]
        public string NewPostCreatedAlias { get; set; }
    }

}
