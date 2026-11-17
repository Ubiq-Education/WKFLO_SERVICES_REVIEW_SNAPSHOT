using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models;
using Mapster;
using Microsoft.SemanticKernel;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentChatMessageResult : IMap
    {
        public string Id { get; set; }
        public string MessageId { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string ImageData { get; set; }
        /// <summary>
        /// US 7974: UTC instant (ISO 8601 with offset) of the message, from its persisted metadata. Null when the
        /// message was stored without one; never 0001-01-01.
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }
        public string Rating { get; set; }
        public ChannelMessageType ChannelMessageType { get; set; }
        public string TopicName { get; set; }
        public List<KernelContentResult> ContentItems { get; set; } = new List<KernelContentResult>();

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<ChatMessageContent, TenantAgentChatMessageResult>()
        //                 .ForMember(dest => dest.Id, source => source.MapFrom(m => m.Role.ToString().ToLower() + "-" + Guid.NewGuid()))
        //                 .ForMember(dest => dest.MessageId, source => source.MapFrom(m => GetMessageIdFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.Type, source => source.MapFrom(m => m.Role.ToString().ToLower()))
        //                 .ForMember(dest => dest.Content, source => source.MapFrom(m => m.Content))
        //                 .ForMember(dest => dest.ImageData, source => source.MapFrom(m => m.Items.OfType<ImageContent>().Select(m => m.DataUri).FirstOrDefault()))
        //                 .ForMember(dest => dest.Timestamp, source => source.MapFrom(m => GetTimestampFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.ChannelMessageType, source => source.MapFrom(m => GetChatMessageTypeFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.TopicName, source => source.MapFrom(m => GetTopicNameFromMetadata(m.Metadata)))
        //                 .ForMember(dest => dest.ContentItems, source => source.MapFrom(m => m.Items));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
#pragma warning disable SKEXP0001
            config.NewConfig<ChatMessageContent, TenantAgentChatMessageResult>()
                .Map(dest => dest.Id, src => src.Role.ToString().ToLower() + "-" + Guid.NewGuid())
                .Map(dest => dest.MessageId, src => GetMessageIdFromMetadata(src.Metadata))
                .Map(dest => dest.Type, src => src.Role.ToString().ToLower())
                .Map(dest => dest.Content, src => src.Content)
                .Map(dest => dest.ImageData, src => src.Items.OfType<ImageContent>().Select(i => i.DataUri).FirstOrDefault())
                .Map(dest => dest.Timestamp, src => GetTimestampFromMetadata(src.Metadata))
                .Map(dest => dest.ChannelMessageType, src => GetChatMessageTypeFromMetadata(src.Metadata))
                .Map(dest => dest.TopicName, src => GetTopicNameFromMetadata(src.Metadata))
                .Map(dest => dest.ContentItems, src => src.Items);
#pragma warning restore SKEXP0001
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

        private static ChannelMessageType GetChatMessageTypeFromMetadata(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.ChannelMessageType, out var chatMessageTypeObj) || chatMessageTypeObj == null)
                return ChannelMessageType.Normal;

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
    }
}
