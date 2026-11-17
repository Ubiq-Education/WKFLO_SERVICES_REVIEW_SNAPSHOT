using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.WebOrigins.Commands.SyncTenantWebOrigins;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.WebOrigins.Commands.SyncTenantWebOrigins
{
    public class SyncTenantWebOriginsCommandHandlerTests(SyncTenantWebOriginsCommandHandlerTesting testing) : IClassFixture<SyncTenantWebOriginsCommandHandlerTesting>
    {
        private SyncTenantWebOriginsCommandHandlerTesting Testing => testing;

        [Theory]
        [InlineData("SignalR", "https://app.example.com", true, ResultType.Ok)]
        [InlineData("SignalR", "https://other.example.com", false, ResultType.Ok)]
        public async Task TestHandler(string webOriginType, string name, bool isActive, ResultType resultType)
        {
            // Act
            var result = await Testing.SyncTenantWebOriginsCommandHandler.Handle(new SyncTenantWebOriginsCommand()
            {
                WebOrigins = new List<SyncTenantWebOriginItem>
                {
                    new SyncTenantWebOriginItem
                    {
                        WebOriginType = webOriginType,
                        Name = name,
                        IsActive = isActive
                    }
                }
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(result.ResultType, resultType);
            if (result is SuccessResult<List<TenantWebOriginResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Single(success.Data);
            }
        }

        [Fact]
        public async Task TestHandler_EmptyList_ReturnsEmptyResult()
        {
            // Act
            var result = await Testing.SyncTenantWebOriginsCommandHandler.Handle(new SyncTenantWebOriginsCommand()
            {
                WebOrigins = new List<SyncTenantWebOriginItem>()
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(ResultType.Ok, result.ResultType);
            if (result is SuccessResult<List<TenantWebOriginResult>> success)
            {
                Assert.NotNull(success.Data);
                Assert.Empty(success.Data);
            }
        }

        private static (SyncTenantWebOriginsCommandHandler Handler, List<WebOrigin> Store, Mock<IMasterDataDbContext> Db) CreateHandler(params WebOrigin[] seed)
        {
            var tenantContext = new TenantContext { TenantName = "Test" };
            var tenant = new Tenant { Id = 1, Name = "Test" };
            var store = seed.ToList();

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.WebOrigins).Returns(() =>
            {
                var snapshot = store.ToList().BuildMockDbSet();
                snapshot.Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<WebOrigin>>(), It.IsAny<CancellationToken>()))
                    .Callback<IEnumerable<WebOrigin>, CancellationToken>((items, _) => store.AddRange(items))
                    .Returns(Task.CompletedTask);
                return snapshot.Object;
            });
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { tenant }.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            return (new SyncTenantWebOriginsCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(), tenantContext), store, db);
        }

        private static SyncTenantWebOriginsCommand Cmd(params (string Type, string Name, bool Active)[] items) => new()
        {
            WebOrigins = items.Select(i => new SyncTenantWebOriginItem { WebOriginType = i.Type, Name = i.Name, IsActive = i.Active }).ToList()
        };

        private static List<TenantWebOriginResult> Data(Result result) =>
            Assert.IsType<SuccessResult<List<TenantWebOriginResult>>>(result).Data;

        [Fact]
        public async Task Sync_StagingThenProduction_KeepsBothSets()
        {
            var (handler, store, db) = CreateHandler();

            await handler.Handle(Cmd(("SignalR", "https://staging.example.com", true)), TestContext.Current.CancellationToken);
            var result = await handler.Handle(Cmd(("SignalR", "https://prod.example.com", true)), TestContext.Current.CancellationToken);

            var data = Data(result);
            Assert.Equal(2, data.Count);
            Assert.Contains(data, d => d.Name == "https://staging.example.com");
            Assert.Contains(data, d => d.Name == "https://prod.example.com");
            Assert.Equal(2, store.Count);
            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Sync_SameListTwice_IsIdempotent()
        {
            var (handler, store, _) = CreateHandler();
            var cmd = Cmd(("SignalR", "https://a.example.com", true), ("SignalR", "https://b.example.com", true));

            await handler.Handle(cmd, TestContext.Current.CancellationToken);
            var result = await handler.Handle(cmd, TestContext.Current.CancellationToken);

            Assert.Equal(2, Data(result).Count);
            Assert.Equal(2, store.Count);
        }

        [Fact]
        public async Task Sync_SoftDeletedMatch_IsReactivated()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = false, IsDeleted = true };
            var (handler, store, db) = CreateHandler(existing);

            var result = await handler.Handle(Cmd(("SignalR", "https://a.example.com", true)), TestContext.Current.CancellationToken);

            db.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(existing.IsDeleted);
            Assert.True(existing.IsActive);
            Assert.Single(store);
            Assert.Single(Data(result));
        }

        [Fact]
        public async Task Sync_InactiveMatch_IsReactivated()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = false };
            var (handler, store, _) = CreateHandler(existing);

            await handler.Handle(Cmd(("SignalR", "https://a.example.com", true)), TestContext.Current.CancellationToken);

            Assert.True(existing.IsActive);
            Assert.False(existing.IsDeleted);
            Assert.Single(store);
        }

        [Fact]
        public async Task Sync_ActiveMatch_IsLeftUnchanged()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = true };
            var (handler, store, _) = CreateHandler(existing);

            await handler.Handle(Cmd(("SignalR", "https://a.example.com", false)), TestContext.Current.CancellationToken);

            Assert.True(existing.IsActive);
            Assert.Single(store);
        }

        [Fact]
        public async Task Sync_CaseAndTrailingSlashVariants_DoNotDuplicate()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = true };
            var (handler, store, _) = CreateHandler(existing);

            var result = await handler.Handle(Cmd(
                ("signalr", "HTTPS://A.Example.com/", true),
                ("SignalR", " https://a.example.com ", true),
                ("SignalR", "https://B.example.com/", true),
                ("SignalR", "https://b.example.com", true)), TestContext.Current.CancellationToken);

            Assert.Equal(2, store.Count);
            Assert.Equal(2, Data(result).Count);
            Assert.Contains(store, o => o.Name == "https://B.example.com");
        }

        [Fact]
        public async Task Sync_SameNameDifferentType_AddsSeparateRow()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = true };
            var (handler, store, _) = CreateHandler(existing);

            await handler.Handle(Cmd(("Other", "https://a.example.com", true)), TestContext.Current.CancellationToken);

            Assert.Equal(2, store.Count);
        }

        [Fact]
        public async Task Sync_ExcludesSoftDeletedNotInIncomingFromResult_AndNeverRemoves()
        {
            var deleted = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://old.example.com", IsActive = false, IsDeleted = true };
            var (handler, store, _) = CreateHandler(deleted);

            var result = await handler.Handle(Cmd(("SignalR", "https://new.example.com", true)), TestContext.Current.CancellationToken);

            Assert.Equal(2, store.Count);
            Assert.True(deleted.IsDeleted);
            Assert.Single(Data(result));
        }

        [Fact]
        public async Task Sync_InactiveRequestForDeletedMatch_DoesNotReactivate()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = false, IsDeleted = true };
            var (handler, store, _) = CreateHandler(existing);

            var result = await handler.Handle(Cmd(("SignalR", "https://a.example.com", false)), TestContext.Current.CancellationToken);

            Assert.True(existing.IsDeleted);
            Assert.False(existing.IsActive);
            Assert.Single(store);
            Assert.Empty(Data(result));
        }

        [Fact]
        public async Task Sync_LegacyNameWithTrailingSlash_IsNormalizedOnMatch()
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com/ ", IsActive = true };
            var (handler, store, _) = CreateHandler(existing);

            await handler.Handle(Cmd(("SignalR", "https://a.example.com", true)), TestContext.Current.CancellationToken);

            Assert.Equal("https://a.example.com", existing.Name);
            Assert.Single(store);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task Sync_DuplicatesWithMixedIsActive_ReactivateRegardlessOfOrder(bool firstActive, bool secondActive)
        {
            var existing = new WebOrigin { Id = 1, TenantId = 1, WebOriginType = "SignalR", Name = "https://a.example.com", IsActive = false, IsDeleted = true };
            var (handler, store, _) = CreateHandler(existing);

            await handler.Handle(Cmd(
                ("SignalR", "https://a.example.com", firstActive),
                ("SignalR", "https://A.example.com/", secondActive)), TestContext.Current.CancellationToken);

            Assert.False(existing.IsDeleted);
            Assert.True(existing.IsActive);
            Assert.Single(store);
        }
    }
}
