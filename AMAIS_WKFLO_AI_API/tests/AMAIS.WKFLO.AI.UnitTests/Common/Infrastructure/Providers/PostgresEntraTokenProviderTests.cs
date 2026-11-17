using AMAIS.WKFLO.Common.Infrastructure.Providers;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.Infrastructure.Providers
{
    /// <summary>
    /// BUG 7880: the PostgreSQL Entra token must be cached from its own expiry, not for a fixed 55 minutes.
    /// PostgresEntraTokenProvider is internal — see the Common.Infrastructure AssemblyInfo.cs's
    /// InternalsVisibleTo("AMAIS.WKFLO.AI.UnitTests").
    /// </summary>
    public class PostgresEntraTokenProviderTests
    {
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<TokenCredential> _credential = new();
        private readonly Mock<TimeProvider> _timeProvider = new();
        private readonly PostgresEntraTokenProvider _provider;
        private DateTimeOffset _now = Start;
        private int _tokenCounter;

        public PostgresEntraTokenProviderTests()
        {
            _timeProvider.Setup(t => t.GetUtcNow()).Returns(() => _now);
            _provider = new PostgresEntraTokenProvider(_credential.Object, _timeProvider.Object, NullLogger.Instance);
        }

        [Fact]
        public async Task GetPasswordAsync_TokenStillValid_ReturnsCachedTokenWithoutRefetching()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));

            var first = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(30);
            var second = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", first);
            Assert.Equal(first, second);
            VerifyFetchedTimes(1);
        }

        [Fact]
        public async Task GetPasswordAsync_WithinRefreshMarginOfExpiry_FetchesNewToken()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));

            await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            // 4 minutes before expiry: inside the default 5-minute margin.
            _now = Start.AddMinutes(56);
            var refreshed = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-2", refreshed);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_JustOutsideRefreshMargin_KeepsCachedToken()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));

            await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(54);
            var cached = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", cached);
            VerifyFetchedTimes(1);
        }

        [Fact]
        public async Task GetPasswordAsync_AfterExpiry_FetchesNewToken()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));

            await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(90);
            var refreshed = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-2", refreshed);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_TokenFetchedWithLittleLifetimeLeft_IsRefreshedBeforeItExpiresNotAfter55Minutes()
        {
            // The BUG 7880 scenario: a dev credential hands back an already-cached token with only 10 minutes left.
            SetupTokensExpiringIn(TimeSpan.FromMinutes(10));

            var first = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            _now = Start.AddMinutes(4);
            var stillCached = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            // 6 minutes in, the first token has 4 minutes left: inside the margin, so it must be replaced
            // (the old fixed 55-minute timer would have kept serving it until well after it expired).
            _now = Start.AddMinutes(6);
            var refreshed = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", first);
            Assert.Equal("token-1", stillCached);
            Assert.Equal("token-2", refreshed);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_RefreshOnEarlierThanMargin_RefreshesAtRefreshOn()
        {
            _credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<AccessToken>(new AccessToken(
                    $"token-{Interlocked.Increment(ref _tokenCounter)}",
                    _now.AddMinutes(60),
                    refreshOn: _now.AddMinutes(30))));

            await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(31);
            var refreshed = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-2", refreshed);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_ConcurrentCallersWithEmptyCache_TriggerSingleFetch()
        {
            var release = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            _credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref _tokenCounter);
                    return new ValueTask<AccessToken>(release.Task);
                });

            // Called directly (no Task.Run) so the contention is deterministic: the first call takes the free
            // semaphore synchronously and suspends inside the pending credential call while holding it; the
            // other nine find the cache empty and suspend on the semaphore. All ten are parked before release.
            var callers = Enumerable.Range(0, 10)
                .Select(_ => _provider.GetPasswordAsync(TestContext.Current.CancellationToken).AsTask())
                .ToArray();

            Assert.Equal(1, _tokenCounter);
            Assert.All(callers, caller => Assert.False(caller.IsCompleted));

            release.SetResult(new AccessToken("shared-token", Start.AddMinutes(60)));
            var results = await Task.WhenAll(callers);

            Assert.All(results, token => Assert.Equal("shared-token", token));
            Assert.Equal(1, _tokenCounter);
        }

        [Fact]
        public async Task GetPasswordAsync_TokenFetchedWithLessThanMarginLeft_IsCachedForHalfItsRemainingLifetime()
        {
            // 4 minutes left is less than the 5-minute margin: the margin is capped at half of it (2 minutes).
            SetupTokensExpiringIn(TimeSpan.FromMinutes(4));

            var first = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(1);
            var stillCached = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            _now = Start.AddMinutes(3);
            var refreshed = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", first);
            Assert.Equal("token-1", stillCached);
            Assert.Equal("token-2", refreshed);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_TokenAlreadyExpiredWhenFetched_IsReturnedButNotCached()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(-1));

            var first = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);
            var second = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", first);
            Assert.Equal("token-2", second);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPasswordAsync_CredentialThrows_PropagatesCachesNothingAndNextCallFetches()
        {
            var failure = new InvalidOperationException("credential unavailable");
            _credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<AccessToken>(Task.FromException<AccessToken>(failure)));

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _provider.GetPasswordAsync(TestContext.Current.CancellationToken).AsTask());
            Assert.Same(failure, thrown);

            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));
            // Bounded wait: if the semaphore had not been released, this call would hang instead of failing.
            var recovered = await _provider.GetPasswordAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("token-1", recovered);
            VerifyFetchedTimes(2);
        }

        [Fact]
        public async Task GetPassword_Sync_CredentialThrows_PropagatesCachesNothingAndNextCallFetches()
        {
            var failure = new InvalidOperationException("credential unavailable");
            _credential
                .Setup(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Throws(failure);

            var thrown = Assert.Throws<InvalidOperationException>(() => _provider.GetPassword());
            Assert.Same(failure, thrown);

            _credential
                .Setup(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new AccessToken($"token-{Interlocked.Increment(ref _tokenCounter)}", _now.AddMinutes(60)));
            // Bounded wait: if the semaphore had not been released, this call would block instead of failing.
            var recovered = await Task.Run(_provider.GetPassword, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("token-1", recovered);
            _credential.Verify(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void GetPassword_Sync_UsesSyncCredentialAndSharesCache()
        {
            _credential
                .Setup(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new AccessToken($"token-{Interlocked.Increment(ref _tokenCounter)}", _now.AddMinutes(60)));

            var first = _provider.GetPassword();
            _now = Start.AddMinutes(30);
            var second = _provider.GetPassword();

            Assert.Equal("token-1", first);
            Assert.Equal(first, second);
            _credential.Verify(c => c.GetToken(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
            _credential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetPasswordAsync_RequestsPostgresEntraScope()
        {
            SetupTokensExpiringIn(TimeSpan.FromMinutes(60));

            await _provider.GetPasswordAsync(TestContext.Current.CancellationToken);

            _credential.Verify(c => c.GetTokenAsync(
                It.Is<TokenRequestContext>(ctx => ctx.Scopes.SequenceEqual(new[] { "https://ossrdbms-aad.database.windows.net/.default" })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private void SetupTokensExpiringIn(TimeSpan lifetime) =>
            _credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<AccessToken>(
                    new AccessToken($"token-{Interlocked.Increment(ref _tokenCounter)}", _now.Add(lifetime))));

        private void VerifyFetchedTimes(int times) =>
            _credential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(times));
    }
}
