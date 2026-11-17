using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples
{
    public class CreateTenantAgentExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Tenant Agent Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "Agent Name",
                            "description": "Agent Description",
                            "systemPrompt": "You are a helpful assistant.",
                            "additionalInstructions": "Always use a warm, welcoming tone.",
                            "isActive": true,
                            "enableSTM": true,
                            "enableLTM": true,
                            "aiModelId": 1
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateTenantAgentExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Tenant Agent Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "name": "Agent Name 2",
                            "description": "Agent Description 2",
                            "systemPrompt": "You are a helpful assistant 2.",
                            "additionalInstructions": "Use a warm, welcoming tone. Keep answers under 100 words.",
                            "isActive": false,
                            "enableSTM": true,
                            "enableLTM": false,
                            "aiModelId": 2
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    /// <summary>WI 7575: PUT api/v1/tenant/agent/{id}/tools/permissions.</summary>
    public class UpdateTenantAgentToolPermissionsExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent/{id}/tools/permissions" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "One permission for an HTTP tool, per-function permissions for an MCP tool",
                    Value = JsonNode.Parse("""
                        {
                            "tools": [
                                { "toolId": 12, "permission": "Ask" },
                                {
                                    "toolId": 15,
                                    "functions": [
                                        { "name": "search_documents", "permission": "Allow" },
                                        { "name": "delete_document", "permission": "Block" }
                                    ]
                                }
                            ]
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class CreateMarketplaceAgentExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/marketplace/agent" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Marketplace Agent Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "Agent Name",
                            "description": "Agent Description",
                            "systemPrompt": "You are a helpful assistant.",
                            "aiModelId": 1
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateMarketplaceAgentExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/marketplace/agent/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Marketplace Agent Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "name": "Agent Name 2",
                            "description": "Agent Description 2",
                            "systemPrompt": "You are a helpful assistant 2.",
                            "aiModelId": 1
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class CreateTenantAgentChatExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent/{id}/chat" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Tenant Agent Chat Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "sessionId": "e9ce1d3b-7506-4f0f-922a-97fb10c63d47"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class CreateAgentChatSettingExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent/{agentid}/chat-setting" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Agent Chat Setting Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "primaryColor": "#007bff",
                            "bubbleStyle": "rounded",
                            "bubbleIconPath": "https://cdn.example.com/chat/bubble.svg",
                            "chatbotIconPath": "https://cdn.example.com/chat/bot.svg",
                            "cssUrl": "https://cdn.example.com/chat/widget.css",
                            "welcomeMessage": "Hello, How can I assist you today",
                            "showDescription": true,
                            "chainOfThought": true,
                            "isWidget": true,
                            "apiEndpoint": "https://api.example.com/chat",
                            "securityConfiguration": "{\"type\": \"public\"}"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateAgentChatSettingExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/agent/{id}/chat-setting" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Agent Chat Setting Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "primaryColor": "#28a745",
                            "bubbleStyle": "square",
                            "bubbleIconPath": "https://cdn.example.com/chat/bubble-alt.svg",
                            "chatbotIconPath": "https://cdn.example.com/chat/bot-alt.svg",
                            "cssUrl": "https://cdn.example.com/chat/widget-alt.css",
                            "welcomeMessage": "Hello, How can I assist you today",
                            "showDescription": false,
                            "chainOfThought": false,
                            "isWidget": false,
                            "apiEndpoint": "https://api.example.com/chat/v2",
                            "securityConfiguration": "{\"type\": \"apikey\", \"value\": \"secret\"}"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }
}
