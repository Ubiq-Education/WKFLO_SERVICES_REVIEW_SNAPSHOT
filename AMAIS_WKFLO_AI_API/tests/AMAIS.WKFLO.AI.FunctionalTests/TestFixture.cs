using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using Dapr.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.FunctionalTests
{
    public sealed class TestFixture : IDisposable
    {
        private readonly TestServer _server;
        public HttpClient Client { get; private set; }
        public IServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        /// Fakes the real <c>DaprClient</c> singleton (registered via <c>services.AddDaprClient()</c> in
        /// <c>ServiceCollectionExtensions</c>) so tests exercising handlers that call
        /// <c>SaveStateAsync</c>/<c>PublishEventAsync</c> (e.g. <c>TriggerMultiAgentOrchestratorCommandHandler</c>)
        /// don't need a live Dapr sidecar. <c>DaprClient</c> is deliberately an abstract class with
        /// virtual/abstract members in the Dapr .NET SDK — designed to be mockable — so a plain
        /// <see cref="Mock{DaprClient}"/> works without any adapter/interface indirection. Defaults to
        /// completed tasks for every call; individual tests can override specific setups via this mock
        /// or use <see cref="Moq.Mock{T}.Verify"/> to assert what was published.
        /// </summary>
        public Mock<DaprClient> DaprClientMock { get; } = new();

        public TestFixture()
        {
            var settings = new Settings
            {
                SymmetricFuncTestKey = "key for functional tests key for functional tests"
            };

            // SaveStateAsync<TValue> is intentionally left unconfigured: it's called with a different
            // closed generic type per command (e.g. WkfloQueueMessage<TriggerMultiAgentOrchestratorCommand>),
            // and Moq's default loose-mock behavior already returns a completed Task for any
            // unconfigured Task-returning virtual/abstract member, for every generic instantiation.

            DaprClientMock.Setup(x => x.PublishEventAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .UseStartup<Setup>()
                        .ConfigureTestServices(services =>
                        {
                            // Change authentication to use bearer token which signed by symmetric key
                            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                            {
                                options.TokenValidationParameters = new TokenValidationParameters
                                {
                                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SymmetricFuncTestKey)),
                                    ValidateIssuerSigningKey = true,
                                    ValidateIssuer = true,
                                    ValidateAudience = true,
                                    ValidateLifetime = true,
                                    ValidIssuer = TestConstants.Issuer,
                                    ValidAudience = TestConstants.Audience,
                                };
                            });

                            // Replace the real DaprClient (which requires a live Dapr sidecar) with a mock
                            services.Replace(ServiceDescriptor.Singleton(DaprClientMock.Object));

                            // The document store reaches the Postgres database; no functional test uses it
                            services.Replace(ServiceDescriptor.Singleton<IChatDocumentStore, NullChatDocumentStore>());
                        })
                        .ConfigureAppConfiguration((hostingContext, configBuilder) =>
                        {
                            hostingContext.HostingEnvironment.EnvironmentName = "Development";

                            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                            Environment.SetEnvironmentVariable("Application", "AMAIS_WLFLO_MD_TEST");

                            configBuilder
                                // Placeholder values for external-service settings that AddInfrastructure binds
                                // and, in a few cases (AddDatastreamProvider, AddAmaisDatastreamProvider,
                                // AddVectorStoreConfiguration), uses eagerly at
                                // service-registration time (e.g. `new Uri(settings.Url)`), before any test gets
                                // a chance to run — so a developer machine/CI with no real appsettings.Development.json
                                // (gitignored local secrets) would otherwise crash building the test host entirely.
                                // None of these external services (Elasticsearch, Azure OpenAI embeddings) are
                                // actually exercised by the functional test suite, so harmless placeholders are
                                // enough. Added first so a real local appsettings.Development.json, if present,
                                // still overrides these with real values.
                                .AddInMemoryCollection(new Dictionary<string, string>
                                {
                                    ["ElasticsearchSettings:Url"] = "https://placeholder.invalid",
                                    ["ElasticsearchSettings:ApiKey"] = "placeholder",
                                    ["EisElasticsearchSettings:Url"] = "https://placeholder.invalid",
                                    ["EisElasticsearchSettings:ApiKey"] = "placeholder",
                                    ["EmbeddingSettings:DeploymentName"] = "placeholder",
                                    ["EmbeddingSettings:Endpoint"] = "https://placeholder.openai.azure.com",
                                    ["CorsSettings:PolicyName"] = "AllowAll"
                                })
                                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .Build();
                        });
                })
                .Start();

            _server = host.GetTestServer();
            Client = _server.CreateClient();

            var scope = _server.Services.CreateScope();

            ServiceProvider = scope.ServiceProvider;
            var dbContext = (MasterDataDbContext)scope.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            dbContext.Database.EnsureCreated();

            dbContext.Tenants.Add(new Tenant
            {
                Name = "Test",
                Region = "UK"
            });

            dbContext.SaveChanges();
        }

        public void AddTenantContext(string tenantName)
        {
            var tenantContext = ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.TenantName = tenantName;
        }

        public IWkfloMediator GetMediator()
        {
            return ServiceProvider.GetRequiredService<IWkfloMediator>();
        }

        public IMasterDataDbContext GetDbContext()
        {
            return ServiceProvider.GetRequiredService<IMasterDataDbContext>();
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }
}
