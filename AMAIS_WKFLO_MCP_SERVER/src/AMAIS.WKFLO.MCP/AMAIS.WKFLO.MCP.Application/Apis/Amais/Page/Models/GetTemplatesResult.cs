using AMAIS.WKFLO.MCP.Application.Apis.Amais.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models
{
    [DataContract]
    public class GetTemplatesResult : BaseResult
    {
        [DataMember(Name = CommonMetadataConsts.ResultType, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.ResultType)]
        public int ResultType { get; set; }

        [DataMember(Name = CommonMetadataConsts.Data, EmitDefaultValue = false)]
        [JsonProperty(PropertyName = CommonMetadataConsts.Data)]
        public ListTemplateData Data { get; set; }
    }

    [DataContract]
    public class ListTemplateData
    {
        [DataMember(Name = "templates", EmitDefaultValue = false)]
        [JsonProperty("templates")]
        public IReadOnlyList<GetTemplateData> Templates { get; set; }
    }

    [DataContract]
    public class GetTemplateData
    {
        [DataMember(Name = "templateName", EmitDefaultValue = false)]
        [JsonProperty("templateName")]
        public string TemplateName { get; set; }

        [DataMember(Name = "pageCount", EmitDefaultValue = false)]
        [JsonProperty("pageCount")]
        public int PageCount { get; set; }
    }
}
