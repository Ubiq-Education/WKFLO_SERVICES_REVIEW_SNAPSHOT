using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Agents.Workflows
{
    /// <summary>
    /// Real (temporary) SQLite database, as in <see cref="ConversationAnalysisWorkflowActivityTests"/>: recording a purge
    /// uses <c>ExecuteUpdateAsync</c>, which MockQueryable/Moq can't translate. Blob storage is mocked.
    /// </summary>
    public class PurgeChatImagesWorkflowActivityTests : IAsyncDisposable
    {
        private const string Container = ChatImageBlobNames.ContainerName;

        // 2026-10-22T00:00Z: UTC midnight; Europe/London (BST) is at 01:00 of Oct 22; America/New_York at 20:00 of Oct 21.
        private static readonly DateTimeOffset UtcMidnight = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateOnly Oct21 = new(2026, 10, 21);
        private static readonly DateOnly Oct22 = new(2026, 10, 22);

        private static readonly Guid UtcTenantGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid LondonTenantGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid NewYorkTenantGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private readonly SqliteConnection _connection;
        private readonly MasterDataDbContext _db;
        private readonly Mock<IBlobStorageProvider> _blobMock = new();
        private readonly Mock<IDateTimeProvider> _clockMock = new();
        private readonly Dictionary<string, List<BlobListItem>> _blobsByPrefix = [];
        private readonly List<string> _deleted = [];
        private readonly PurgeChatImagesWorkflowActivity _activity;

        public PurgeChatImagesWorkflowActivityTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();

            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_connection).Options;
            _db = new MasterDataDbContext(options, _clockMock.Object);
            _db.Database.EnsureCreated();

            _clockMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(UtcMidnight);

            _blobMock
                .Setup(x => x.ListBlobsAsync(Container, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string prefix, CancellationToken _) =>
                    prefix.Length == 0
                        ? _blobsByPrefix.Values.SelectMany(b => b).ToList()
                        : _blobsByPrefix.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).SelectMany(p => p.Value).ToList());

            _blobMock
                .Setup(x => x.DeleteAsync(Container, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string name, CancellationToken _) => _deleted.Add(name))
                .ReturnsAsync(true);

            _activity = new PurgeChatImagesWorkflowActivity(_blobMock.Object, _db, _clockMock.Object,
                NullLogger<PurgeChatImagesWorkflowActivity>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        // ==================== Helpers ====================

        private async Task<int> SeedTenantAsync(Guid guid, string timeZone, DateOnly? lastPurge)
        {
            var tenant = new Tenant { Name = $"tenant-{guid:N}", Guid = guid };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

            if (timeZone is not null)
                _db.TenantSettings.Add(new TenantSetting { TenantId = tenant.Id, TimeZone = timeZone });

            if (lastPurge is not null)
                _db.ChatImagePurgeStates.Add(new ChatImagePurgeState { TenantId = tenant.Id, LastPurgeLocalDate = lastPurge.Value });

            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return tenant.Id;
        }

        private async Task SeedUnownedStateAsync(DateOnly lastPurge)
        {
            _db.ChatImagePurgeStates.Add(new ChatImagePurgeState { TenantId = null, LastPurgeLocalDate = lastPurge });
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private async Task SeedConversationAsync(string sessionId, int tenantId)
        {
            _db.Conversations.Add(new Conversation { SessionId = sessionId, TenantId = tenantId, AgentId = 1 });
            await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private void AddBlob(string name, DateTimeOffset? createdOn)
        {
            var key = name;
            if (!_blobsByPrefix.TryGetValue(key, out var list))
                _blobsByPrefix[key] = list = [];
            list.Add(new BlobListItem(name, createdOn));
        }

        private static string TenantBlob(Guid tenantGuid, string file) => $"{tenantGuid:D}/session-x/{file}";

        private async Task<DateOnly?> LastPurgeAsync(int? tenantId) =>
            await _db.ChatImagePurgeStates.AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .Select(p => (DateOnly?)p.LastPurgeLocalDate)
                .SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        private void VerifyListedPrefix(Guid tenantGuid, Times times) =>
            _blobMock.Verify(x => x.ListBlobsAsync(Container, ChatImageBlobNames.TenantPrefix(tenantGuid), It.IsAny<CancellationToken>()), times);

        // ==================== Tenant scopes ====================

        [Fact]
        public async Task PurgeAsync_PurgesOnlyDueTenants_AndKeepsBlobsCreatedFromTheCutoff()
        {
            var utcTenant = await SeedTenantAsync(UtcTenantGuid, timeZone: null, lastPurge: Oct21);
            var londonTenant = await SeedTenantAsync(LondonTenantGuid, "Europe/London", lastPurge: Oct22);
            var newYorkTenant = await SeedTenantAsync(NewYorkTenantGuid, "America/New_York", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);

            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddSeconds(-1));
            AddBlob(TenantBlob(UtcTenantGuid, "today.png"), UtcMidnight);
            AddBlob(TenantBlob(UtcTenantGuid, "unknown-age.png"), null);
            AddBlob(TenantBlob(LondonTenantGuid, "old.png"), UtcMidnight.AddDays(-2));
            AddBlob(TenantBlob(NewYorkTenantGuid, "old.png"), UtcMidnight.AddDays(-2));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal([TenantBlob(UtcTenantGuid, "old.png")], _deleted);
            VerifyListedPrefix(UtcTenantGuid, Times.Once());
            VerifyListedPrefix(LondonTenantGuid, Times.Never());
            VerifyListedPrefix(NewYorkTenantGuid, Times.Never());

            Assert.Equal(Oct22, await LastPurgeAsync(utcTenant));
            Assert.Equal(Oct22, await LastPurgeAsync(londonTenant));
            Assert.Equal(Oct21, await LastPurgeAsync(newYorkTenant));
        }

        [Fact]
        public async Task PurgeAsync_UsesTenantTimeZoneForCutoff()
        {
            // 23:30Z on Oct 21 is 00:30 of Oct 22 in London (BST): due, cutoff 23:00Z.
            var now = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.Zero);
            await SeedTenantAsync(LondonTenantGuid, "Europe/London", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct21);

            AddBlob(TenantBlob(LondonTenantGuid, "before.png"), new DateTimeOffset(2026, 10, 21, 22, 59, 0, TimeSpan.Zero));
            AddBlob(TenantBlob(LondonTenantGuid, "after.png"), new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero));

            await _activity.PurgeAsync(now, TestContext.Current.CancellationToken);

            Assert.Equal([TenantBlob(LondonTenantGuid, "before.png")], _deleted);
        }

        [Fact]
        public async Task PurgeAsync_FirstRun_NoState_PurgesAndRecordsDay()
        {
            var tenantId = await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: null);
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddHours(-5));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Single(_deleted);
            Assert.Equal(Oct22, await LastPurgeAsync(tenantId));
            Assert.Equal(Oct22, await LastPurgeAsync(null));
        }

        [Fact]
        public async Task PurgeAsync_SecondTickSameDay_DoesNothing()
        {
            await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct21);
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddHours(-5));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);
            _blobMock.Invocations.Clear();
            _deleted.Clear();

            await _activity.PurgeAsync(UtcMidnight.AddHours(1), TestContext.Current.CancellationToken);

            Assert.Empty(_deleted);
            _blobMock.Verify(x => x.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PurgeAsync_InvalidTimeZone_PurgesAtUtcMidnight()
        {
            var tenantId = await SeedTenantAsync(UtcTenantGuid, "Not/AZone", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddSeconds(-1));
            AddBlob(TenantBlob(UtcTenantGuid, "today.png"), UtcMidnight.AddSeconds(1));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal([TenantBlob(UtcTenantGuid, "old.png")], _deleted);
            Assert.Equal(Oct22, await LastPurgeAsync(tenantId));
        }

        [Fact]
        public async Task PurgeAsync_DeleteFails_DayIsNotRecorded_SoNextTickRetries()
        {
            var tenantId = await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);
            AddBlob(TenantBlob(UtcTenantGuid, "stuck.png"), UtcMidnight.AddHours(-1));
            _blobMock
                .Setup(x => x.DeleteAsync(Container, TenantBlob(UtcTenantGuid, "stuck.png"), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage unavailable"));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal(Oct21, await LastPurgeAsync(tenantId));
            _blobMock.Invocations.Clear();

            await _activity.PurgeAsync(UtcMidnight.AddHours(1), TestContext.Current.CancellationToken);

            VerifyListedPrefix(UtcTenantGuid, Times.Once());
        }

        [Fact]
        public async Task PurgeAsync_DeleteFails_LogsTenantButNotTheBlobName()
        {
            await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);
            var blobName = TenantBlob(UtcTenantGuid, "holiday-photo.png");
            AddBlob(blobName, UtcMidnight.AddHours(-1));
            _blobMock
                .Setup(x => x.DeleteAsync(Container, blobName, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("storage unavailable"));

            var loggerMock = new Mock<ILogger<PurgeChatImagesWorkflowActivity>>();
            var activity = new PurgeChatImagesWorkflowActivity(_blobMock.Object, _db, _clockMock.Object, loggerMock.Object);

            await activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            loggerMock.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("failed to delete a chat image of tenant")), It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            loggerMock.Verify(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("holiday-photo") || v.ToString().Contains("session-x")), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [Fact]
        public async Task PurgeAsync_DayRecordedByAnotherRunMeanwhile_ConditionalUpdateDoesNotMoveItBack()
        {
            var tenantId = await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddHours(-1));

            // Another replica records a later day while this run is purging.
            _blobMock
                .Setup(x => x.ListBlobsAsync(Container, ChatImageBlobNames.TenantPrefix(UtcTenantGuid), It.IsAny<CancellationToken>()))
                .Callback(() => _db.Database.ExecuteSqlRaw("UPDATE ChatImagePurgeState SET LastPurgeLocalDate = '2026-10-23' WHERE TenantId = {0}", tenantId))
                .ReturnsAsync([]);

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal(new DateOnly(2026, 10, 23), await LastPurgeAsync(tenantId));
        }

        /// <summary>
        /// The other run's row is already there when this run checks for it, so the existence check skips the insert.
        /// This does NOT cover the insert racing into SQL Server's unique-key violation (2627/2601): that branch is
        /// covered by the IsUniqueKeyViolation* tests, and a non-duplicate insert failure by
        /// <see cref="PurgeAsync_InsertFailsForAnotherReason_DayIsNotRecorded"/>.
        /// </summary>
        [Fact]
        public async Task PurgeAsync_RowInsertedByAnotherRunBeforeExistenceCheck_SkipsInsertAndKeepsSingleRow()
        {
            var tenantId = await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: null);
            await SeedUnownedStateAsync(Oct22);

            _blobMock
                .Setup(x => x.ListBlobsAsync(Container, ChatImageBlobNames.TenantPrefix(UtcTenantGuid), It.IsAny<CancellationToken>()))
                .Callback(() => _db.Database.ExecuteSqlRaw(
                    "INSERT INTO ChatImagePurgeState (TenantId, LastPurgeLocalDate, IsDeleted, CreatedOn) VALUES ({0}, '2026-10-22', 0, '2026-10-22 00:00:00+00:00')", tenantId))
                .ReturnsAsync([]);

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal(1, await _db.ChatImagePurgeStates.CountAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken));
            Assert.Equal(Oct22, await LastPurgeAsync(tenantId));
        }

        [Fact]
        public async Task PurgeAsync_NothingDue_DoesNotListStorage()
        {
            await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct22);
            await SeedUnownedStateAsync(Oct22);

            await _activity.PurgeAsync(UtcMidnight.AddHours(3), TestContext.Current.CancellationToken);

            _blobMock.Verify(x => x.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(_deleted);
        }

        // ==================== Legacy (tenant-less) blob names ====================

        [Fact]
        public async Task PurgeAsync_LegacySessionBlobs_UseTheConversationTenantsCutoff()
        {
            var utcTenant = await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct21);
            var newYorkTenant = await SeedTenantAsync(NewYorkTenantGuid, "America/New_York", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct22);
            await SeedConversationAsync("session-utc", utcTenant);
            await SeedConversationAsync("session-ny", newYorkTenant);

            AddBlob("session-utc/old.png", UtcMidnight.AddHours(-2));
            AddBlob("session-utc/today.png", UtcMidnight.AddMinutes(1));
            AddBlob("session-ny/old.png", UtcMidnight.AddDays(-2));
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), UtcMidnight.AddHours(-2));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            // New York is not due yet (20:00 of Oct 21), so its legacy blob stays; the tenant-prefixed blob is deleted once.
            Assert.Equal([TenantBlob(UtcTenantGuid, "old.png"), "session-utc/old.png"], _deleted.Order(StringComparer.Ordinal).ToList());
            _blobMock.Verify(x => x.ListBlobsAsync(Container, string.Empty, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PurgeAsync_UnmappedLegacyAndEditedBlobs_ArePurgedAtUtcMidnight()
        {
            await SeedTenantAsync(UtcTenantGuid, "UTC", lastPurge: Oct22);

            AddBlob("edited/old.png", UtcMidnight.AddHours(-3));
            AddBlob("edited/today.png", UtcMidnight.AddMinutes(5));
            AddBlob("unknown-session/old.png", UtcMidnight.AddHours(-3));
            AddBlob("44444444-4444-4444-4444-444444444444/edited/old.png", UtcMidnight.AddHours(-3));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal(
                ["44444444-4444-4444-4444-444444444444/edited/old.png", "edited/old.png", "unknown-session/old.png"],
                _deleted.Order(StringComparer.Ordinal).ToList());
            Assert.Equal(Oct22, await LastPurgeAsync(null));
        }

        [Fact]
        public async Task PurgeAsync_UnownedScopeNotDue_KeepsEditedBlobs_EvenWhenATenantIsDue()
        {
            // 19:00Z on Oct 21: Asia/Kolkata is already on Oct 22, UTC is still on Oct 21.
            var now = new DateTimeOffset(2026, 10, 21, 19, 0, 0, TimeSpan.Zero);
            await SeedTenantAsync(UtcTenantGuid, "Asia/Kolkata", lastPurge: Oct21);
            await SeedUnownedStateAsync(Oct21);

            AddBlob("edited/old.png", now.AddDays(-1));
            AddBlob(TenantBlob(UtcTenantGuid, "old.png"), now.AddHours(-1));

            await _activity.PurgeAsync(now, TestContext.Current.CancellationToken);

            Assert.Equal([TenantBlob(UtcTenantGuid, "old.png")], _deleted);
            Assert.Equal(Oct21, await LastPurgeAsync(null));
        }

        [Fact]
        public async Task PurgeAsync_TenantBlobWithSlashInSessionId_IsNeverInTheNoTenantScope_OnlyPurgedWithItsTenantsCutoff()
        {
            // 00:00Z on Oct 22: the no-tenant (UTC) scope is due; New York (20:00 of Oct 21) is not.
            var newYorkTenant = await SeedTenantAsync(NewYorkTenantGuid, "America/New_York", lastPurge: Oct21);
            var deepBlob = $"{NewYorkTenantGuid:D}/session/with/slashes/image.png";
            AddBlob(deepBlob, UtcMidnight.AddHours(-1));

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Empty(_deleted);
            Assert.Equal(Oct21, await LastPurgeAsync(newYorkTenant));

            // At New York's midnight (04:00Z) the tenant prefix listing deletes it, as it is older than that cutoff.
            await _activity.PurgeAsync(UtcMidnight.AddHours(4), TestContext.Current.CancellationToken);

            Assert.Equal([deepBlob], _deleted);
            Assert.Equal(Oct22, await LastPurgeAsync(newYorkTenant));
        }

        [Fact]
        public async Task PurgeAsync_LegacySessionSharedByTenantsEastAndWestOfUtc_UsesTheEarliestDayStartOfAllOfThem()
        {
            // Asia/Kolkata (east, Oct 22 starts 18:30Z on Oct 21) and America/New_York (west, Oct 21 starts 04:00Z,
            // Oct 22 starts 04:00Z on Oct 22) both have a conversation with the same session id.
            var kolkataTenant = await SeedTenantAsync(UtcTenantGuid, "Asia/Kolkata", lastPurge: Oct21);
            var newYorkTenant = await SeedTenantAsync(NewYorkTenantGuid, "America/New_York", lastPurge: new DateOnly(2026, 10, 20));
            await SeedUnownedStateAsync(Oct22);
            await SeedConversationAsync("shared-session", kolkataTenant);
            await SeedConversationAsync("shared-session", newYorkTenant);

            AddBlob("shared-session/before-both.png", new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero));
            AddBlob("shared-session/new-york-today.png", new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero));
            AddBlob("shared-session/kolkata-today.png", new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.Zero));

            // 19:00Z on Oct 21: Kolkata is due (Oct 22); New York is on Oct 21 and its "today" images must survive.
            var kolkataMidnightTick = new DateTimeOffset(2026, 10, 21, 19, 0, 0, TimeSpan.Zero);
            _clockMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(kolkataMidnightTick);
            await _activity.PurgeAsync(kolkataMidnightTick, TestContext.Current.CancellationToken);

            Assert.Contains("shared-session/before-both.png", _deleted);
            Assert.DoesNotContain("shared-session/new-york-today.png", _deleted);
            Assert.DoesNotContain("shared-session/kolkata-today.png", _deleted);
            Assert.Equal(Oct22, await LastPurgeAsync(kolkataTenant));
            _deleted.Clear();

            // 04:00Z on Oct 22: New York's midnight. The Oct 21 New York image goes; the Oct 22 Kolkata image stays.
            var newYorkMidnightTick = new DateTimeOffset(2026, 10, 22, 4, 0, 0, TimeSpan.Zero);
            _clockMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(newYorkMidnightTick);
            await _activity.PurgeAsync(newYorkMidnightTick, TestContext.Current.CancellationToken);

            Assert.Contains("shared-session/new-york-today.png", _deleted);
            Assert.DoesNotContain("shared-session/kolkata-today.png", _deleted);
        }

        // ==================== Insert race classification ====================

        [Theory]
        [InlineData(2627, true)]
        [InlineData(2601, true)]
        [InlineData(547, false)]
        [InlineData(1205, false)]
        [InlineData(0, false)]
        public void IsUniqueKeyViolationNumber_OnlyDuplicateKeyErrors(int number, bool expected)
        {
            Assert.Equal(expected, PurgeChatImagesWorkflowActivity.IsUniqueKeyViolationNumber(number));
        }

        [Fact]
        public void IsUniqueKeyViolation_DbUpdateExceptionWithoutSqlServerDuplicateKey_IsNotBenign()
        {
            var ex = new DbUpdateException("save failed", new InvalidOperationException("connection reset"));

            Assert.False(PurgeChatImagesWorkflowActivity.IsUniqueKeyViolation(ex));
        }

        [Fact]
        public async Task PurgeAsync_InsertFailsForAnotherReason_DayIsNotRecorded()
        {
            // SQLite with foreign keys on: inserting the state row for a tenant id that does not exist fails with a
            // non-duplicate-key error, which must leave the day unrecorded rather than be treated as "another run won".
            await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options;
            await using var db = new MasterDataDbContext(options, _clockMock.Object);
            db.Database.EnsureCreated();
            db.Tenants.Add(new Tenant { Name = "tenant-fk", Guid = UtcTenantGuid });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChatImagePurgeStates.Add(new ChatImagePurgeState { TenantId = null, LastPurgeLocalDate = Oct22 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var tenantId = await db.Tenants.Select(t => t.Id).SingleAsync(TestContext.Current.CancellationToken);
            _blobMock
                .Setup(x => x.ListBlobsAsync(Container, ChatImageBlobNames.TenantPrefix(UtcTenantGuid), It.IsAny<CancellationToken>()))
                .Callback(() => db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF; DELETE FROM Tenant; PRAGMA foreign_keys = ON;"))
                .ReturnsAsync([]);

            var loggerMock = new Mock<ILogger<PurgeChatImagesWorkflowActivity>>();
            var activity = new PurgeChatImagesWorkflowActivity(_blobMock.Object, db, _clockMock.Object, loggerMock.Object);

            await activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.False(await db.ChatImagePurgeStates.AnyAsync(p => p.TenantId == tenantId, TestContext.Current.CancellationToken));
            loggerMock.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<DbUpdateException>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
            loggerMock.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("already recorded")), It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
        }

        [Fact]
        public async Task PurgeAsync_UppercaseTenantGuidName_IsNotSkippedByBothPaths_PurgedAsNoTenant()
        {
            // The tenant prefix listing is case-sensitive (lowercase Guid), so an uppercase-Guid name is only seen by the
            // legacy listing, where it falls into the no-tenant (UTC) scope instead of being skipped.
            var letterGuid = Guid.Parse("abcdefab-cdef-abcd-efab-cdefabcdefab");
            await SeedTenantAsync(letterGuid, "America/New_York", lastPurge: Oct21);
            var upperName = $"{letterGuid.ToString("D").ToUpperInvariant()}/session-x/image.png";
            _blobsByPrefix[upperName] = [new BlobListItem(upperName, UtcMidnight.AddHours(-1))];

            await _activity.PurgeAsync(UtcMidnight, TestContext.Current.CancellationToken);

            Assert.Equal([upperName], _deleted);
        }

        // ==================== Tick time ====================

        [Fact]
        public void ResolveNowUtc_UsesTheLaterOfTickAndClock()
        {
            Assert.Equal(UtcMidnight.AddMinutes(2), _activity.ResolveNowUtc(UtcMidnight.AddMinutes(2).UtcDateTime.ToString("O")));
            Assert.Equal(UtcMidnight, _activity.ResolveNowUtc(UtcMidnight.AddHours(-1).UtcDateTime.ToString("O")));
            Assert.Equal(UtcMidnight, _activity.ResolveNowUtc(string.Empty));
            Assert.Equal(UtcMidnight, _activity.ResolveNowUtc("not a date"));
        }
    }
}
