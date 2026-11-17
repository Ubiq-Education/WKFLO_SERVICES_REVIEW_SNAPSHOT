using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Models.Results.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore
{
    public class ChatStoreService : IChatStoreService
    {
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly ILogger<ChatStoreService> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ChatStoreService(IBlobStorageProvider blobStorageProvider, ILogger<ChatStoreService> logger, IDateTimeProvider dateTimeProvider)
        {
            _blobStorageProvider = blobStorageProvider;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
        }

        private string GetBlobName(string tenantName, int agentId, string sessionId, string userId)
        {
            var userGuid = userId ?? Guid.Empty.ToString();
            // safe path: tenant/user/agent/session.json
            return $"{Uri.EscapeDataString(tenantName)}/{userGuid}/{agentId}/{Uri.EscapeDataString(sessionId)}.json";
        }

        public async Task<(int thumbsUp, int thumbsDown)> SaveChatAsync(string tenantName, int agentId, string sessionId, string name, ChatHistory chatHistory, DateTimeOffset createdAt, string userId)
        {
            var userGuid = userId ?? Guid.Empty.ToString();
            var blobName = GetBlobName(tenantName, agentId, sessionId, userGuid);

            // Preserve existing message ratings — do not erase them when the chat history is updated
            Dictionary<string, string> existingRatings = null;
            if (await _blobStorageProvider.ExistsAsync("chat-history", blobName))
            {
                try
                {
                    var download = await _blobStorageProvider.DownloadAsync("chat-history", blobName, default);
                    var reader = new System.IO.StreamReader(download);
                    var existing = JsonSerializer.Deserialize<PersistedChat>(await reader.ReadToEndAsync());
                    existingRatings = existing?.MessageRatings;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read existing blob to preserve ratings for tenant: {TenantName}, agent: {AgentId}, session: {SessionId}", tenantName, agentId, sessionId);
                }
            }

            var persistedChat = new PersistedChat
            {
                AgentId = agentId,
                SessionId = sessionId,
                TenantName = tenantName,
                UserGuid = userGuid,
                Name = name,
                CreatedAt = createdAt,
                UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow(),
                SerializedChatHistory = JsonSerializer.Serialize(chatHistory),
                MessageRatings = existingRatings ?? new Dictionary<string, string>()
            };

            await _blobStorageProvider.UploadAsync("chat-history", blobName, new System.IO.MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(persistedChat))), "application/json", default);

            var thumbsUp = persistedChat.MessageRatings.Values.Count(v => v == "thumbsUp");
            var thumbsDown = persistedChat.MessageRatings.Values.Count(v => v == "thumbsDown");
            return (thumbsUp, thumbsDown);
        }

        public async Task<(DateTimeOffset, string, ChatHistory, Dictionary<string, string>)> LoadChatAsync(string tenantName, int agentId, string sessionId, string userId)
        {
            var userGuid = userId ?? Guid.Empty.ToString();
            if (!await _blobStorageProvider.ExistsAsync("chat-history", GetBlobName(tenantName, agentId, sessionId, userGuid))) return (_dateTimeProvider.DateTimeOffsetUtcNow(), null, [], null);
            // Download blob
            var download = await _blobStorageProvider.DownloadAsync("chat-history", GetBlobName(tenantName, agentId, sessionId, userGuid), default);
            var reader = new System.IO.StreamReader(download);
            // Deserialize PersistedChat
            var persisted = JsonSerializer.Deserialize<PersistedChat>(await reader.ReadToEndAsync());
            if (persisted == null) return (_dateTimeProvider.DateTimeOffsetUtcNow(), null, [], null);
            // Deserialize ChatHistory back
            var history = JsonSerializer.Deserialize<ChatHistory>(persisted.SerializedChatHistory!);
            if (history == null) return (_dateTimeProvider.DateTimeOffsetUtcNow(), null, [], null);

            // Replace plain chat-images blob URLs with SAS URLs (routed through CDN if configured)
            // so the AI provider can access them. If SAS generation fails, fall back to downloading the bytes.
            await ReplaceChatImageUrlsWithSasAsync(history);

            return (persisted.CreatedAt, persisted.Name, history, persisted.MessageRatings?.Count > 0 ? persisted.MessageRatings : null);
        }

        private async Task ReplaceChatImageUrlsWithSasAsync(ChatHistory history)
        {
            foreach (var message in history)
            {
                var expiredItems = new List<Microsoft.SemanticKernel.ImageContent>();

                foreach (var item in message.Items.OfType<Microsoft.SemanticKernel.ImageContent>())
                {
                    if (item.Uri == null || !item.Uri.AbsolutePath.Contains("/chat-images/"))
                        continue;

                    try
                    {
                        var blobPath = string.Concat(item.Uri.Segments.Skip(2).Select(Uri.UnescapeDataString));
                        var exists = await _blobStorageProvider.ExistsAsync("chat-images", blobPath);
                        if (!exists)
                        {
                            // Expected once the daily chat-images purge has removed the blob; the image is
                            // replaced with a text placeholder below, so this is not worth a warning.
                            _logger.LogDebug("Image blob no longer exists: {ImageUri}. Marking as expired.", item.Uri);
                            expiredItems.Add(item);
                            continue;
                        }

                        // Generate SAS URL (with CDN host if configured in BlobStorageProvider)
                        var sasUrl = await _blobStorageProvider.GenerateSasUrlAsync(item.Uri, TimeSpan.FromHours(1));
                        item.Uri = new Uri(sasUrl);
                        item.Data = null; // Clear any existing data to ensure the provider uses the URI
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process image: {ImageUri}. Image has expired and will be removed from context.", item.Uri);
                        expiredItems.Add(item);
                    }
                }

                // Replace expired image items with a text placeholder so the AI provider
                // doesn't attempt to fetch a broken URI and throw an exception.
                foreach (var expired in expiredItems)
                {
                    var index = message.Items.IndexOf(expired);
                    if (index >= 0)
                        message.Items[index] = new Microsoft.SemanticKernel.TextContent("[Image no longer available — the uploaded image has expired]");
                }
            }
        }

        public async Task<List<ChatListResult>> ListChatsAsync(string tenantName, int agentId, string userId)
        {
            try
            {
                var userGuid = userId ?? Guid.Empty.ToString();
                var prefix = $"{Uri.EscapeDataString(tenantName)}/{userGuid}/{agentId}/";
                var persistedChats = await _blobStorageProvider.ListAsync<PersistedChat>("chat-history", prefix);

                return persistedChats
                    .Select(pc => new ChatListResult
                    {
                        AgentId = pc.AgentId,
                        SessionId = pc.SessionId,
                        Name = pc.Name,
                        CreatedAt = pc.CreatedAt
                    })
                    .OrderByDescending(c => c.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list chats for tenant: {TenantName}, agent: {AgentId}", tenantName, agentId);
                return new List<ChatListResult>();
            }
        }

        public async Task<(bool found, int thumbsUp, int thumbsDown)> RateMessageAsync(string tenantName, int agentId, string sessionId, string userId, string messageId, bool isThumbsUp)
        {
            try
            {
                var userGuid = userId ?? Guid.Empty.ToString();
                var blobName = GetBlobName(tenantName, agentId, sessionId, userGuid);

                if (!await _blobStorageProvider.ExistsAsync("chat-history", blobName))
                    return (false, 0, 0);

                var download = await _blobStorageProvider.DownloadAsync("chat-history", blobName, default);
                var reader = new System.IO.StreamReader(download);
                var persisted = JsonSerializer.Deserialize<PersistedChat>(await reader.ReadToEndAsync());
                if (persisted == null) return (false, 0, 0);

                persisted.MessageRatings ??= new Dictionary<string, string>();
                persisted.MessageRatings[messageId] = isThumbsUp ? "thumbsUp" : "thumbsDown";
                persisted.UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow();

                await _blobStorageProvider.UploadAsync("chat-history", blobName,
                    new System.IO.MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(persisted))),
                    "application/json", default);

                var thumbsUp = persisted.MessageRatings.Values.Count(v => v == "thumbsUp");
                var thumbsDown = persisted.MessageRatings.Values.Count(v => v == "thumbsDown");
                return (true, thumbsUp, thumbsDown);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rate message for tenant: {TenantName}, agent: {AgentId}, session: {SessionId}", tenantName, agentId, sessionId);
                return (false, 0, 0);
            }
        }

        public Task<bool> DeleteChatAsync(string tenantName, int agentId, string sessionId, string userId)
        {
            try
            {
                var userGuid = userId ?? Guid.Empty.ToString();
                return _blobStorageProvider.DeleteAsync("chat-history", GetBlobName(tenantName, agentId, sessionId, userGuid));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete chat for tenant: {TenantName}, agent: {AgentId}, session: {SessionId}", tenantName, agentId, sessionId);
                return Task.FromResult(false);
            }
        }
    }
}
