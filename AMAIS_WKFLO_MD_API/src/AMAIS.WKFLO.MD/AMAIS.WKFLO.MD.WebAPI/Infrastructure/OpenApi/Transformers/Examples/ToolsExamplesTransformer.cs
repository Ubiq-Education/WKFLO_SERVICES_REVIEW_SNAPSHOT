using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples
{
    public class CreateTenantToolExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/tool" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Tenant Tool Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "Tool Name",
                            "description": "Tool Description",
                            "type": "HTTP",
                            "config": "{}",
                            "isActive": true
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateTenantToolExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/tool/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Tenant Tool Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "name": "Tool Name 2",
                            "description": "Tool Description 2",
                            "type": "Python",
                            "config": "{}",
                            "isActive": false,
                            "permissionsEnabled": true
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    /// <summary>WI 7447: PUT api/v1/tenant/tool/{id}/functions.</summary>
    public class UpdateTenantToolFunctionsExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant/tool/{id}/functions" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Enable two MCP functions (every other function is disabled)",
                    Value = JsonNode.Parse("""
                        {
                            "enabledFunctions": [ "search_documents", "get_document" ]
                        }
                        """)
                },
                ["WithDisplayValues"] = new OpenApiExample
                {
                    Summary = "Also set friendly display values (optional; omit 'functions' to leave them unchanged)",
                    Value = JsonNode.Parse("""
                        {
                            "enabledFunctions": [ "search_documents", "get_document" ],
                            "functions": [
                                { "name": "search_documents", "displayName": "Search documents", "displayDescription": "Finds documents that match a question." },
                                { "name": "get_document", "displayName": null, "displayDescription": null }
                            ]
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class CreateMarketplaceToolExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/marketplace/tool" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Marketplace Tool Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "Tool Name",
                            "description": "Tool Description",
                            "type": "HTTP",
                            "config": "{}"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateMarketplaceToolExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/marketplace/tool/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Marketplace Tool Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "name": "Tool Name 2",
                            "description": "Tool Description 2",
                            "type": "Python",
                            "config": "{}"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }
}
