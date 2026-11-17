using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text;

namespace AMAIS.WKFLO.MD.FunctionalTests
{
    public sealed class TestFixture : IDisposable
    {
        private readonly TestServer _server;
        public HttpClient Client { get; private set; }
        public IServiceProvider ServiceProvider { get; private set; }

        public TestFixture()
        {
            var settings = new Settings
            {
                SymmetricFuncTestKey = "key for functional tests key for functional tests"
            };

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

                            // Replace real vector storage with a no-op to avoid Neon PostgreSQL calls in tests
                            services.Replace(ServiceDescriptor.Singleton<IVectorStorageProvider, NullVectorStorageProvider>());
                            services.Replace(ServiceDescriptor.Singleton<IChatDocumentStore, NullChatDocumentStore>());
                        })
                        .ConfigureAppConfiguration((hostingContext, configBuilder) =>
                        {
                            hostingContext.HostingEnvironment.EnvironmentName = "Development";

                            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                            Environment.SetEnvironmentVariable("Application", "AMAIS_WLFLO_MD_TEST");

                            configBuilder
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

            dbContext.Tenants.Add(new Tenant
            {
                Name = "Test2",
                Region = "UK"
            });

            dbContext.SaveChanges();
        }

        public void AddTenantContext(string tenantName)
        {
            var tenantContext = ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.TenantName = tenantName;
        }

        public void AddUserContext(string email, string userName, string userId)
        {
            var dbContext = (MasterDataDbContext)ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var user = new User
            {
                Email = email
            };
            dbContext.Users.Add(user);
            dbContext.SaveChanges();

            var userContext = ServiceProvider.GetRequiredService<UserContext>();
            userContext.Email = email;
            userContext.UserName = userName;
            userContext.UserId = userId;
            userContext.Guid = user.Id.ToString();
            userContext.Claims = new List<ClaimContext>
            {
                new ClaimContext(ClaimTypes.Email, email),
                new ClaimContext(ClaimTypes.Name, userName),
                new ClaimContext(ClaimTypes.NameIdentifier, userId)
            };
            userContext.Roles = new List<string>();
        }

        public IWkfloMediator GetMediator()
        {
            return ServiceProvider.GetRequiredService<IWkfloMediator>();
        }

        public IMasterDataDbContext GetDbContext()
        {
            return (MasterDataDbContext)ServiceProvider.GetRequiredService<IMasterDataDbContext>();
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }
}
