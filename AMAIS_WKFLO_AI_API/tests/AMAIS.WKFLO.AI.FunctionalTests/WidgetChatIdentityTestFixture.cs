using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Data;
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
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.FunctionalTests
{
    /// <summary>
    /// Hosts <see cref="WidgetChatIdentitySetup"/> — the production-equivalent pipeline — with both
    /// the default scheme and the AmaisJwtScheme overridden to symmetric test keys, the same
    /// technique <see cref="TestFixture"/> already uses for the default scheme.
    /// </summary>
    public sealed class WidgetChatIdentityTestFixture : IDisposable
    {
        private readonly IHost _host;
        private readonly TestServer _server;
        private readonly IServiceScope _scope;
        public HttpClient Client { get; }
        public IServiceProvider ServiceProvider { get; }
        public Mock<DaprClient> DaprClientMock { get; } = new();

        public WidgetChatIdentityTestFixture()
        {
            const string defaultSchemeKey = "key for functional tests key for functional tests";

            DaprClientMock.Setup(x => x.PublishEventAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .UseStartup<WidgetChatIdentitySetup>()
                        .ConfigureTestServices(services =>
                        {
                            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                            {
                                options.TokenValidationParameters = new TokenValidationParameters
                                {
                                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(defaultSchemeKey)),
                                    ValidateIssuerSigningKey = true,
                                    ValidateIssuer = true,
                                    ValidateAudience = true,
                                    ValidateLifetime = true,
                                    ValidIssuer = "Issuer",
                                    ValidAudience = "Audience",
                                };
                            });

                            services.PostConfigure<JwtBearerOptions>(AmaisJwtConst.SchemeName, options =>
                            {
                                options.TokenValidationParameters = new TokenValidationParameters
                                {
                                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AmaisTestTokenBuilder.SymmetricKey)),
                                    ValidateIssuerSigningKey = true,
                                    ValidateIssuer = true,
                                    ValidIssuer = AmaisTestTokenBuilder.Issuer,
                                    ValidateAudience = false,
                                    ValidateLifetime = true,
                                    TryAllIssuerSigningKeys = true,
                                };
                            });

                            services.Replace(ServiceDescriptor.Singleton(DaprClientMock.Object));
                        })
                        .ConfigureAppConfiguration((hostingContext, configBuilder) =>
                        {
                            hostingContext.HostingEnvironment.EnvironmentName = "Development";

                            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                            Environment.SetEnvironmentVariable("Application", "AMAIS_WKFLO_AI_WIDGET_IDENTITY_TEST");

                            configBuilder
                                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .Build();
                        });
                })
                .Start();

            _server = _host.GetTestServer();
            Client = _server.CreateClient();

            _scope = _server.Services.CreateScope();
            ServiceProvider = _scope.ServiceProvider;
            var dbContext = (MasterDataDbContext)ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            dbContext.Database.EnsureCreated();
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
            _scope.Dispose();
            Client.Dispose();
            _server.Dispose();
            _host.Dispose();
        }
    }
}
