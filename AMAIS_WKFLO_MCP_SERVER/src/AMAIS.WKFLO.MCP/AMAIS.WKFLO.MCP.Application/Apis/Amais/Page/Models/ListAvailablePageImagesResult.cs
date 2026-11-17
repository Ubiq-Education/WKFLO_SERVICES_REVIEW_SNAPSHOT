using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class ListAvailablePageImagesResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public ListAvailablePageImagesData Data { get; set; }
    }

    [DataContract]
    public class ListAvailablePageImagesData
    {
        [DataMember(Name = CommonPageMetadataConsts.Images, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.Images)]
        public List<AvailablePageImagesData> Images { get; set; }

    }

    [DataContract]
    public class AvailablePageImagesData
    {
        [DataMember(Name = CommonPageMetadataConsts.RenditionId, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonPageMetadataConsts.RenditionId)]
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
