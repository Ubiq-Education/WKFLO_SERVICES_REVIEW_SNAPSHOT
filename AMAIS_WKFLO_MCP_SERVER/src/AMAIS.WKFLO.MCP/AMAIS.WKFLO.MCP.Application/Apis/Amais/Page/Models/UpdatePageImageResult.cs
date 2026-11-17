using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class UpdatePageImageResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public UpdatePageImageData Data { get; set; }
    }

    [DataContract]
    public class UpdatePageImageData
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

        [DataMember(Name = CommonPageMetadataConsts.PageImageName, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.PageImageName)]
        public string PageImageName { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.PageImagePath, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.PageImagePath)]
        public string PageImagePath { get; set; }

        [DataMember(Name = CommonPageMetadataConsts.RenditionId, EmitDefaultValue = false)]
        [JsonProperty(CommonPageMetadataConsts.RenditionId)]
        public int RenditionId { get; set; }
    }

}
