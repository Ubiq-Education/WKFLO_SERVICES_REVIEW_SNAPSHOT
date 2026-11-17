using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples
{
    public class CreateSecretExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/secret" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Secret Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "apiKey": "your-api-key",
                            "name": "Name API Key",
                            "isActive": true
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateSecretExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/secret/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update Secret Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "apiKey": "your-api-key-2",
                            "name": "Name API Key 2",
                            "isActive": false
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }
}
