using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using AMAIS.WKFLO.MD.UnitTests.Application.Agents.Common;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tenants.Commands.CreateTenant
{
    [Collection(DefaultWebOriginsEnvironmentCollection.Name)]
    public class CreateTenantCommandHandlerTests(CreateTenantCommandHandlerTesting testing) : IClassFixture<CreateTenantCommandHandlerTesting>, IDisposable
    {
        private const string DefaultWebOriginsVariable = "DEFAULT_WEB_ORIGINS";

        // Captured per test (xUnit creates a new instance per test) and restored in Dispose,
        // so clearing the variable in CreateExistingTenantHandler never leaks to other tests.
        private readonly string _originalDefaultWebOrigins = Environment.GetEnvironmentVariable(DefaultWebOriginsVariable);

        private CreateTenantCommandHandlerTesting Testing => testing;

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(DefaultWebOriginsVariable, _originalDefaultWebOrigins);
            GC.SuppressFinalize(this);
        }

        [Theory]
        [InlineData("NewTenant1", "UK", ResultType.Ok, 201)]
        [InlineData("NewTenant2", "US", ResultType.Ok, 201)]
        public async Task TestHandler_NewTenant_ReturnsCreated(string name, string region, ResultType resultType, int expectedCode)
        {
            // Act
            var result = await Testing.CreateTenantCommandHandler.Handle(new CreateTenantCommand
            {
                Name = name,
                Region = region
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(resultType, result.ResultType);
            if (result is SuccessResult<TenantCreateResult> success)
            {
                Assert.Equal(expectedCode, success.Data.Code);
                Assert.Equal(name, success.Data.Name);
                Assert.Equal(region, success.Data.Region);
            }
        }

        [Fact]
        public async Task TestHandler_NewTenant_WithWebOrigins_ReturnsWebOrigins()
        {
            // Act
            var result = await Testing.CreateTenantCommandHandler.Handle(new CreateTenantCommand
            {
                Name = "NewTenantWithOrigins",
                Region = "US",
                WebOrigins = new List<CreateTenantWebOriginItem>
                {
                    new() { WebOriginType = "SignalR", Name = "https://app.example.com", IsActive = true }
                }
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<TenantCreateResult> success)
            {
                Assert.Equal(201, success.Data.Code);
                Assert.NotNull(success.Data.WebOrigins);
                Assert.Single(success.Data.WebOrigins);
                Assert.Equal("SignalR", success.Data.WebOrigins[0].WebOriginType);
                Assert.Equal("https://app.example.com", success.Data.WebOrigins[0].Name);
            }
        }

        [Fact]
        public async Task TestHandler_ExistingTenant_NoWebOrigins_KeepsExistingWebOrigins()
        {
            // Act
            var result = await Testing.CreateTenantCommandHandler.Handle(new CreateTenantCommand
            {
                Name = CreateTenantCommandHandlerTesting.ExistingTenantName,
                Region = "EU"
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<TenantCreateResult> success)
            {
                Assert.Equal(200, success.Data.Code);
                Assert.NotNull(success.Data.WebOrigins);
                Assert.Contains(success.Data.WebOrigins, o => o.Name == "https://existing.example.com");
            }
        }

        /// <summary>
        /// Builds the handler with DEFAULT_WEB_ORIGINS cleared so origin counts don't depend on the runner's environment.
        /// The original value is restored in <see cref="Dispose"/>.
        /// </summary>
        private static (CreateTenantCommandHandler Handler, List<WebOrigin> Store) CreateExistingTenantHandler(params WebOrigin[] seed)
        {
            Environment.SetEnvironmentVariable(DefaultWebOriginsVariable, null);
            var store = seed.ToList();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { new() { Id = 1, Name = CreateTenantCommandHandlerTesting.ExistingTenantName, Region = "EU" } }.BuildMockDbSet().Object);
            db.Setup(x => x.WebOrigins).Returns(() =>
            {
                var snapshot = store.ToList().BuildMockDbSet();
                snapshot.Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<WebOrigin>>(), It.IsAny<CancellationToken>()))
                    .Callback<IEnumerable<WebOrigin>, CancellationToken>((items, _) => store.AddRange(items))
                    .Returns(Task.CompletedTask);
                return snapshot.Object;
            });
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            return (new CreateTenantCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper()), store);
        }

        private static WebOrigin Origin(int id, string name, bool active = true, bool deleted = false) =>
            new() { Id = id, TenantId = 1, WebOriginType = "SignalR", Name = name, IsActive = active, IsDeleted = deleted };

        private static CreateTenantCommand ExistingCmd(params string[] names) => new()
        {
            Name = CreateTenantCommandHandlerTesting.ExistingTenantName,
            Region = "EU",
            WebOrigins = names.Select(n => new CreateTenantWebOriginItem { WebOriginType = "SignalR", Name = n, IsActive = true }).ToList()
        };

        [Fact]
        public async Task TestHandler_ExistingTenant_WithWebOrigins_KeepsOldAndAddsNew()
        {
            var (handler, store) = CreateExistingTenantHandler(Origin(1, "https://existing.example.com"));

            var result = await handler.Handle(ExistingCmd("https://new.example.com", "https://new2.example.com"), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantCreateResult>>(result);
            Assert.Equal(200, success.Data.Code);
            Assert.Equal(3, success.Data.WebOrigins.Count);
            Assert.Contains(success.Data.WebOrigins, o => o.Name == "https://existing.example.com");
            Assert.Contains(success.Data.WebOrigins, o => o.Name == "https://new.example.com");
            Assert.Contains(success.Data.WebOrigins, o => o.Name == "https://new2.example.com");
            Assert.Equal(3, store.Count);
        }

        [Fact]
        public async Task TestHandler_ExistingTenant_SameOriginsAgain_NoDuplicates()
        {
            var (handler, store) = CreateExistingTenantHandler(Origin(1, "https://existing.example.com"));

            await handler.Handle(ExistingCmd("https://existing.example.com/", "HTTPS://EXISTING.example.com"), TestContext.Current.CancellationToken);

            Assert.Single(store);
            Assert.Equal("https://existing.example.com", store[0].Name);
        }

        [Fact]
        public async Task TestHandler_ExistingTenant_SoftDeletedMatch_IsReactivated()
        {
            var deleted = Origin(1, "https://existing.example.com", active: false, deleted: true);
            var (handler, store) = CreateExistingTenantHandler(deleted);

            var result = await handler.Handle(ExistingCmd("https://existing.example.com"), TestContext.Current.CancellationToken);

            Assert.False(deleted.IsDeleted);
            Assert.True(deleted.IsActive);
            Assert.Single(store);
            Assert.Single(Assert.IsType<SuccessResult<TenantCreateResult>>(result).Data.WebOrigins);
        }

        [Fact]
        public async Task TestHandler_ExistingTenant_WithWebOrigins_StillAddsDefaults()
        {
            var previous = Environment.GetEnvironmentVariable("DEFAULT_WEB_ORIGINS");
            var (handler, store) = CreateExistingTenantHandler(Origin(1, "https://existing.example.com"));
            Environment.SetEnvironmentVariable("DEFAULT_WEB_ORIGINS", "https://default.example.com");
            try
            {

                var result = await handler.Handle(ExistingCmd("https://new.example.com"), TestContext.Current.CancellationToken);

                var data = Assert.IsType<SuccessResult<TenantCreateResult>>(result).Data.WebOrigins;
                Assert.Equal(3, data.Count);
                Assert.Contains(data, o => o.Name == "https://default.example.com");
                Assert.Contains(data, o => o.Name == "https://existing.example.com");
                Assert.Equal(3, store.Count);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DEFAULT_WEB_ORIGINS", previous);
            }
        }
    }
}
