using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    [McpServerToolType]
    [Authorize]
    public sealed partial class AmaisPageTools
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDateTimeProvider _dateTimeProvider;

        public AmaisPageTools(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor, IDateTimeProvider dateTimeProvider)
        {
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _dateTimeProvider = dateTimeProvider;
        }

        public TextContentBlock GetTextContentBlockForUserNotAuthorized()
        {
            return new TextContentBlock()
            {
                Text = "User is not authenticated.",
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
                }
            };
        }

        public TextContentBlock GetTextContentBlockForEmptyResult()
        {
            return new TextContentBlock()
            {
                Text = "Empty result.",
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
                }
            };
        }

        public TextContentBlock GetTextContentBlockForErrorResult(string errorMessage)
        {
            return new TextContentBlock()
            {
                Text = $"Error: {errorMessage}",
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                    [CommonMetadataConsts.IsError] = true
                }
            };
        }
    }
}
