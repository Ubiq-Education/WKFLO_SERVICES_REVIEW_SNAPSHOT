using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Post.Models
{
    [DataContract]
    public class UpdatePostImageResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public UpdatePostImageData Data { get; set; }
    }

    [DataContract]
    public class UpdatePostImageData
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

        [DataMember(Name = CommonPostMetadataConsts.PostImageName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPostMetadataConsts.PostImageName)]
        public string PostImageName { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.PostImagePath, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.PostImagePath)]
        public string PostImagePath { get; set; }

        [DataMember(Name = CommonPostMetadataConsts.RenditionId, EmitDefaultValue = false)]
        [JsonProperty(CommonPostMetadataConsts.RenditionId)]
        public int RenditionId { get; set; }
    }

}
