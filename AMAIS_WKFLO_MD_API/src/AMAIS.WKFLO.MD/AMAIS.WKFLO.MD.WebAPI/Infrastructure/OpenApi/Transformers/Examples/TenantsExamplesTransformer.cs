using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples
{
    public class CreateTenantExampleTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.RelativePath != "api/v1/tenant" ||
                !string.Equals(context.Description.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            OpenApiMediaType mediaType = null;
            if (!operation.RequestBody?.Content.TryGetValue("application/json", out mediaType) ?? true)
                return Task.CompletedTask;

            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["Example"] = new OpenApiExample
                {
                    Summary = "Create Tenant Command Example",
                    Value = JsonNode.Parse("""
                        {
                            "name": "ExampleTenant",
                            "region": "UK"
                        }
                        """)
                }
            };

            return Task.CompletedTask;
        }
    }
}
