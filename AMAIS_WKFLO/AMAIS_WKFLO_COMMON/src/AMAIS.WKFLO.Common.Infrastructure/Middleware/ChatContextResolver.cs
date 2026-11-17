using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    /// <summary>
    /// Middleware that resolves and sets the ChatContext.SessionId from various request sources.
    /// Attempts to extract SessionId from:
    /// 1. HTTP headers (amais-wkflo-session-id)
    /// 2. Query string parameters (sessionId)
    /// 3. Route values (sessionId)
    /// 4. Request body JSON (sessionId or SessionId properties)
    /// </summary>
    public class ChatContextResolver
    {
        private readonly RequestDelegate _next;

        public ChatContextResolver(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var chatContext = context.RequestServices.GetRequiredService<ChatContext>();

            var pagePath = context.Request.Headers[CommonConsts.PagePath].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(pagePath))
            {
                chatContext.PagePath = pagePath;
            }

            var pageId = context.Request.Headers[CommonConsts.PageId].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(pageId))
            {
                chatContext.PageId = pageId.Trim();
            }

            var timeZone = context.Request.Headers[CommonConsts.TimeZone].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(timeZone))
            {
                chatContext.TimeZone = timeZone;
            }

            var subjectId = context.Request.Headers[CommonConsts.SubjectId].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(subjectId))
            {
                chatContext.SubjectId = subjectId;
            }

            var featureId = context.Request.Headers[CommonConsts.FeatureId].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(featureId))
            {
                chatContext.FeatureId = featureId;
            }

            var featureType = context.Request.Headers[CommonConsts.FeatureType].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(featureType))
            {
                chatContext.FeatureType = featureType;
            }

            var featureName = context.Request.Headers[CommonConsts.FeatureName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(featureName))
            {
                chatContext.FeatureName = featureName;
            }

            var amaisAvailableEndpoints = context.Request.Headers[CommonConsts.AmaisAvailableEndpoints].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(amaisAvailableEndpoints))
            {
                chatContext.AmaisAvailableEndpoints = amaisAvailableEndpoints
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            // Only ever turns this ON — a missing/false header must not clear a value already set earlier in
            // this same scope, matching the "once true, never revert" contract enforced downstream when the
            // Conversation row is persisted.
            var isTestChat = context.Request.Headers[CommonConsts.IsTestChat].FirstOrDefault();
            if (bool.TryParse(isTestChat, out var isTestChatValue) && isTestChatValue)
            {
                chatContext.IsTestChat = true;
            }

            // Try to get SessionId from headers first (highest priority)
            var sessionIdFromHeaders = context.Request.Headers[CommonConsts.SessionId].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sessionIdFromHeaders))
            {
                chatContext.SessionId = sessionIdFromHeaders;
            }

            // Try to get SessionId from query string if not found in headers
            if (string.IsNullOrWhiteSpace(chatContext.SessionId))
            {
                var sessionIdFromQuery = context.Request.Query["sessionId"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(sessionIdFromQuery))
                {
                    chatContext.SessionId = sessionIdFromQuery;
                }
            }

            // Try to get SessionId from route values if it contains sessionId parameter
            if (string.IsNullOrWhiteSpace(chatContext.SessionId))
            {
                var routeData = context.GetRouteData();
                if (routeData?.Values != null && routeData.Values.ContainsKey("sessionId"))
                {
                    var sessionIdFromPath = routeData.Values["sessionId"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(sessionIdFromPath))
                    {
                        chatContext.SessionId = sessionIdFromPath;
                    }
                }
            }

            // Try to get SessionId from request body if it's a JSON request with body content
            if (string.IsNullOrWhiteSpace(chatContext.SessionId) &&
                HasJsonContent(context.Request) &&
                HasBody(context.Request))
            {
                await TryExtractSessionIdFromBody(context, chatContext);
            }

            await _next(context);
        }

        private static bool HasJsonContent(HttpRequest request)
        {
            return request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static bool HasBody(HttpRequest request)
        {
            return request.ContentLength > 0 ||
                   request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                   request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                   request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task TryExtractSessionIdFromBody(HttpContext context, ChatContext chatContext)
        {
            try
            {
                // Enable buffering to allow multiple reads of the request body
                context.Request.EnableBuffering();

                // Read the request body
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

                // Reset the stream position for the next middleware/controller
                context.Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    // Parse JSON to extract SessionId
                    using var document = JsonDocument.Parse(body);

                    // Try camelCase first (common in JavaScript/TypeScript APIs)
                    if (document.RootElement.TryGetProperty("sessionId", out var sessionIdProperty))
                    {
                        var sessionIdFromBody = sessionIdProperty.GetString();
                        if (!string.IsNullOrWhiteSpace(sessionIdFromBody))
                        {
                            chatContext.SessionId = sessionIdFromBody;
                        }
                    }
                    // Try PascalCase if camelCase not found (common in C# APIs)
                    else if (document.RootElement.TryGetProperty("SessionId", out var sessionIdPropertyPascal))
                    {
                        var sessionIdFromBody = sessionIdPropertyPascal.GetString();
                        if (!string.IsNullOrWhiteSpace(sessionIdFromBody))
                        {
                            chatContext.SessionId = sessionIdFromBody;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, ignore and continue
                // This allows the middleware to be resilient to invalid JSON
            }
            catch (Exception)
            {
                // If any other error occurs, ignore and continue
                // This ensures the middleware doesn't break the request pipeline
            }
        }
    }
}
