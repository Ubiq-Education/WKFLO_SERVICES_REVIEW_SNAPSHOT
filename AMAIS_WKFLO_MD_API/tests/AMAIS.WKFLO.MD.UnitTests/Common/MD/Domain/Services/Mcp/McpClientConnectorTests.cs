using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp
{
    public class McpClientConnectorTests
    {
        private static readonly McpClient Client = new Mock<McpClient>().Object;

        private sealed class Recorder
        {
            private readonly Queue<Func<McpClient>> _outcomes;

            public Recorder(params Func<McpClient>[] outcomes) => _outcomes = new Queue<Func<McpClient>>(outcomes);

            public List<string> RequestedVersions { get; } = [];
            public List<IClientTransport> Transports { get; } = [];
            public int TransportsCreated { get; private set; }

            public IClientTransport NewTransport()
            {
                TransportsCreated++;
                return new Mock<IClientTransport>().Object;
            }

            public Task<McpClient> CreateAsync(IClientTransport transport, McpClientOptions options, CancellationToken _)
            {
                Transports.Add(transport);
                RequestedVersions.Add(options?.ProtocolVersion);
                return Task.FromResult(_outcomes.Dequeue()());
            }
        }

        private static Func<McpClient> Http(HttpStatusCode status) =>
            () => throw new HttpRequestException($"HTTP {(int)status}", null, status);

        private static Task<McpClient> Connect(Recorder recorder, McpClientOptions options = null, CancellationToken cancellationToken = default) =>
            McpClientConnector.ConnectAsync(recorder.NewTransport, options, NullLogger.Instance, "Canva", recorder.CreateAsync, cancellationToken);

        [Fact]
        public async Task DefaultSucceeds_NoRetry()
        {
            var recorder = new Recorder(() => Client);

            var client = await Connect(recorder);

            Assert.Same(Client, client);
            Assert.Equal([null], recorder.RequestedVersions);
            Assert.Equal(1, recorder.TransportsCreated);
        }

        [Fact]
        public async Task Http500OnDefaultProbe_RetriesPinnedVersionsInOrderOnFreshTransports()
        {
            var recorder = new Recorder(Http(HttpStatusCode.InternalServerError), Http(HttpStatusCode.InternalServerError), () => Client);

            var client = await Connect(recorder);

            Assert.Same(Client, client);
            Assert.Equal([null, "2025-11-25", "2025-06-18"], recorder.RequestedVersions);
            Assert.Equal(3, recorder.TransportsCreated);
            Assert.Equal(3, new HashSet<IClientTransport>(recorder.Transports).Count);
        }

        [Fact]
        public async Task HttpErrorWrappedInMcpException_IsRetried()
        {
            var recorder = new Recorder(
                () => throw new McpException("probe failed", new HttpRequestException("500", null, HttpStatusCode.InternalServerError)),
                () => Client);

            await Connect(recorder);

            Assert.Equal([null, "2025-11-25"], recorder.RequestedVersions);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task AuthFailure_NotRetried(HttpStatusCode status)
        {
            var recorder = new Recorder(Http(status));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Connect(recorder));

            Assert.Equal(status, ex.StatusCode);
            Assert.Single(recorder.RequestedVersions);
        }

        [Fact]
        public async Task ConnectionFailureWithoutStatus_NotRetried()
        {
            var recorder = new Recorder(() => throw new HttpRequestException("connection refused"));

            await Assert.ThrowsAsync<HttpRequestException>(() => Connect(recorder));

            Assert.Single(recorder.RequestedVersions);
        }

        [Fact]
        public async Task Cancellation_NotRetried()
        {
            using var cts = new CancellationTokenSource();
            var recorder = new Recorder(() =>
            {
                cts.Cancel();
                throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
            });

            await Assert.ThrowsAsync<HttpRequestException>(() => Connect(recorder, cancellationToken: cts.Token));

            Assert.Single(recorder.RequestedVersions);
        }

        [Fact]
        public async Task AllAttemptsFail_RethrowsLastError()
        {
            var last = new McpException("server negotiated 2024-11-05");
            var recorder = new Recorder(
                Http(HttpStatusCode.InternalServerError),
                Http(HttpStatusCode.InternalServerError),
                Http(HttpStatusCode.BadGateway),
                () => throw last);

            var ex = await Assert.ThrowsAsync<McpException>(() => Connect(recorder));

            Assert.Same(last, ex);
            Assert.Equal([null, "2025-11-25", "2025-06-18", "2025-03-26"], recorder.RequestedVersions);
        }

        [Fact]
        public async Task AuthFailureDuringRetries_StopsRetrying()
        {
            var recorder = new Recorder(Http(HttpStatusCode.InternalServerError), Http(HttpStatusCode.Unauthorized));

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Connect(recorder));

            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
            Assert.Equal(2, recorder.RequestedVersions.Count);
        }

        [Fact]
        public async Task CallerPinnedVersion_NotOverriddenOrRetried()
        {
            var recorder = new Recorder(Http(HttpStatusCode.InternalServerError));

            await Assert.ThrowsAsync<HttpRequestException>(() => Connect(recorder, new McpClientOptions { ProtocolVersion = "2025-06-18" }));

            Assert.Equal(["2025-06-18"], recorder.RequestedVersions);
        }

        [Fact]
        public async Task CallerOptions_KeptOnRetries_OnlyProtocolVersionOverridden()
        {
            var options = new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(7), ClientInfo = new() { Name = "wkflo", Version = "1" } };
            McpClientOptions retryOptions = null;
            var calls = 0;

            await McpClientConnector.ConnectAsync(() => new Mock<IClientTransport>().Object, options, NullLogger.Instance, "Canva",
                (_, o, _) =>
                {
                    if (calls++ == 0) throw new HttpRequestException("500", null, HttpStatusCode.InternalServerError);
                    retryOptions = o;
                    return Task.FromResult(Client);
                }, TestContext.Current.CancellationToken);

            Assert.NotSame(options, retryOptions);
            Assert.Null(options.ProtocolVersion);
            Assert.Equal("2025-11-25", retryOptions.ProtocolVersion);
            Assert.Equal(TimeSpan.FromSeconds(7), retryOptions.InitializationTimeout);
            Assert.Same(options.ClientInfo, retryOptions.ClientInfo);
        }
    }
}
