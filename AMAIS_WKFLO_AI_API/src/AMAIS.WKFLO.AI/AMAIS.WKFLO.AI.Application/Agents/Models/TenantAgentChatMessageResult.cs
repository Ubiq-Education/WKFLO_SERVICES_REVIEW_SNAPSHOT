using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models;
using Mapster;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    public class TenantAgentChatMessageResult : IMap
    {
        public string Id { get; set; }
        public string MessageId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public TenantAgentFileResult File { get; set; }
        public string FileUrl { get; set; }
        /// <summary>
        /// US 7974: UTC instant (ISO 8601 with offset) of the message, from its persisted metadata. Null when the
        /// message was stored without one; never 0001-01-01.
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }
        public string Rating { get; set; }
        public int AgentId { get; set; }
        public string AgentName { get; set; }
        public string ChatbotIconUrl { get; set; }
        public ChannelMessageType ChannelMessageType { get; set; }
        public string TopicName { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public List<KernelContentResult> ContentItems { get; set; } = new List<KernelContentResult>();

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<ChatMessageContent, TenantAgentChatMessageResult>()
        //                 .ForMember(dest => dest.Id, source => source.MapFrom(m => m.Role.ToString().ToLower() + "-" + Guid.NewGuid()))
        //                 .ForMember(dest => dest.MessageId, source => source.MapFrom(m => GetMessageIdFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.Type, source => source.MapFrom(m => m.Role.ToString().ToLower()))
        //                 .ForMember(dest => dest.Content, source => source.MapFrom(m => m.Content))
        //                 .ForMember(dest => dest.File, source => source.MapFrom(m => GetFileDataTypeFromItems(m.Items)))
        //                 .ForMember(dest => dest.FileUrl, source => source.MapFrom(m => GetFileUrlFromItems(m.Items)))
        //                 .ForMember(dest => dest.Timestamp, source => source.MapFrom(m => GetTimestampFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.ChannelMessageType, source => source.MapFrom(m => GetChatMessageTypeFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.TopicName, source => source.MapFrom(m => GetTopicNameFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.Metadata, source => source.MapFrom(m => GetMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.ContentItems, source => source.MapFrom(m => m.Items));
        //         }

        private static string GetFileUrlFromItems(ChatMessageContentItemCollection items)
        {
            var imageContent = items.OfType<ImageContent>().FirstOrDefault();
            if (imageContent != null && imageContent.Uri != null)
            {
                return imageContent.Uri.ToString();
            }

            return null;
        }

        private static TenantAgentFileResult GetFileDataTypeFromItems(ChatMessageContentItemCollection items)
        {
            var imageContent = items.OfType<ImageContent>().FirstOrDefault();
            if (imageContent != null && imageContent.DataUri != null && imageContent.MimeType != null)
            {
                return new TenantAgentFileResult
                {
                    Data = imageContent.DataUri,
                    MimeType = imageContent.MimeType
                };
            }

            var textContent = items.OfType<TextContent>().Where(m => m.Metadata != null && m.Metadata.ContainsKey(CommonMetadataConsts.FileName)).FirstOrDefault();
            if (textContent != null)
            {
                return new TenantAgentFileResult
                {
                    Name = textContent.Metadata[CommonMetadataConsts.FileName]?.ToString() ?? "document.txt",
                    MimeType = textContent.Metadata[CommonMetadataConsts.FileType]?.ToString() ?? "text/plain",
                    Size = textContent.Metadata.ContainsKey(CommonMetadataConsts.FileSize) && int.TryParse(textContent.Metadata[CommonMetadataConsts.FileSize]?.ToString(), out var size) ? size : 0
                };
            }

            return null;
        }

        private static string GetMessageIdFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.MessageId, out var messageIdObj) || messageIdObj == null)
                return null;

            return messageIdObj switch
            {
                JsonElement element => element.GetString(),
                string s => s,
                _ => messageIdObj.ToString()
            };
        }

        private static DateTimeOffset? GetTimestampFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.Timestamp, out var timestampObj) || timestampObj == null)
                return null;

            // Manejar diferentes posibles tipos
            DateTimeOffset? timestamp = timestampObj switch
            {
                JsonElement element when DateTimeOffset.TryParse(element.ToString(), out var result) => result,
                DateTimeOffset dto => dto,
                // Before the DateTime case: converting default(DateTime) throws on hosts ahead of UTC.
                DateTime dt when dt == default => null,
                DateTime dt => new DateTimeOffset(dt),
                string s when DateTimeOffset.TryParse(s, out var result) => result,
                _ => null
            };

            // US 7974: always returned in UTC; a stored default value means "no timestamp", not 0001-01-01.
            return timestamp is { } value && value != default ? value.ToUniversalTime() : null;
        }

        private static ChannelMessageType GetChannelMessageTypeFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.ChannelMessageType, out var chatMessageTypeObj) || chatMessageTypeObj == null)
                return ChannelMessageType.Normal;

            // Manejar diferentes posibles tipos
            return chatMessageTypeObj switch
            {
                JsonElement element when Enum.TryParse<ChannelMessageType>(element.ToString(), out var result) => result,
                string mt when Enum.TryParse<ChannelMessageType>(mt, out var result) => result,
                _ => ChannelMessageType.Normal
            };
        }

        private static string GetTopicNameFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.TopicName, out var topicNameObj) || topicNameObj == null)
                return null;

            return topicNameObj switch
            {
                JsonElement element => element.GetString(),
                string s => s,
                _ => topicNameObj.ToString()
            };
        }

        private static Dictionary<string, object> GetMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            var result = new Dictionary<string, object>();
            if (metadata == null)
                return result;

            if (metadata.TryGetValue(CommonMetadataConsts.Url, out var urlObject) && urlObject != null)
            {
                result.Add(CommonMetadataConsts.Url, urlObject.ToString());
            }

            if (metadata.TryGetValue(CommonMetadataConsts.FormType, out var formTypeObject) && formTypeObject != null)
            {
                result.Add(CommonMetadataConsts.FormType, formTypeObject.ToString());
            }

            if (metadata.TryGetValue(CommonMetadataConsts.Data, out var dataObject) && dataObject != null)
            {
                result.Add(CommonMetadataConsts.Data, dataObject.ToString());
            }

            if (metadata.TryGetValue(CommonMetadataConsts.RenderAsSystem, out var renderAsSystemObject) && renderAsSystemObject != null)
            {
                result.Add(CommonMetadataConsts.RenderAsSystem, renderAsSystemObject);
            }

            // US 7502: the "Stopped. All agents cancelled." divider. Plain values, as the blob gives JSON elements back.
            if (StoppedTurnMarker.IsMarker(metadata))
            {
                result[CommonMetadataConsts.IsStoppedMarker] = true;
                if (StoppedTurnMarker.ReadStoppedAt(metadata) is { } stoppedAt)
                    result[CommonMetadataConsts.StoppedAt] = StoppedTurnMarker.FormatStoppedAt(stoppedAt);
            }

            return result;
        }

        public void Mapping(TypeAdapterConfig config)
        {
#pragma warning disable SKEXP0001
            config.NewConfig<ChatMessageContent, TenantAgentChatMessageResult>()
                .Map(dest => dest.Id, src => src.Role.ToString().ToLower() + "-" + Guid.NewGuid())
                .Map(dest => dest.MessageId, src => GetMessageIdFromMetadata(src.Metadata))
                .Map(dest => dest.Type, src => src.Role.ToString().ToLower())
                .Map(dest => dest.Content, src => ChatImageLabel.ContentForChat(src))
                .Map(dest => dest.File, src => GetFileDataTypeFromItems(src.Items))
                .Map(dest => dest.FileUrl, src => GetFileUrlFromItems(src.Items))
                .Map(dest => dest.Timestamp, src => GetTimestampFromMetadata(src.Metadata))
                .Map(dest => dest.ChannelMessageType, src => GetChannelMessageTypeFromMetadata(src.Metadata))
                .Map(dest => dest.TopicName, src => GetTopicNameFromMetadata(src.Metadata))
                .Map(dest => dest.Metadata, src => GetMetadata(src.Metadata))
                .Map(dest => dest.ContentItems, src => src.Items);
#pragma warning restore SKEXP0001
        }
    }

}

