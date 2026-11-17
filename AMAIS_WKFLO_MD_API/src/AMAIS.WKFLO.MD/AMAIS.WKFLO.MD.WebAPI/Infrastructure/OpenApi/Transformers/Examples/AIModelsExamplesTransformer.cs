using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples
{
    public class CreateAIModelExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/ai-model" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create AI Model Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "AI Model Name",
                            "model": "gpt-3.5-turbo",
                            "deploymentName": "gpt-35-deployment",
                            "provider": "OpenAI",
                            "endpoint": "https://openai.example.com",
                            "apiVersion": "2024-01-01",
                            "secretId": 1,
                            "type": "text",
                            "isActive": true
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }

    public class UpdateAIModelExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/ai-model/{id}" ||
                !string.Equals(context.Description.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Update AI Model Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "id": 1,
                            "name": "AI Model Name 2",
                            "model": "gpt-3.5-turbo-2",
                            "deploymentName": "gpt-35-deployment-2",
                            "provider": "OpenAI 2",
                            "endpoint": "https://openai.example.com/v2",
                            "apiVersion": "2024-02-01",
                            "secretId": 2,
                            "type": "text 2",
                            "isActive": false
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }
}
