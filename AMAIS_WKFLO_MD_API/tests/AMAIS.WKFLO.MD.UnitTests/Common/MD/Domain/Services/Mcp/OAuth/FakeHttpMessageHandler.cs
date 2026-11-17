using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth
{
    /// <summary>
    /// Routes outbound requests to a caller-supplied responder by URL, so tests can mock the outbound
    /// <see cref="HttpClient"/> for the hand-rolled MCP OAuth discovery/DCR/token-exchange HTTP calls
    /// without any real network I/O.
    /// </summary>
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        /// <summary>Convenience constructor for tests that only need to map exact URLs to canned string responses.</summary>
        public FakeHttpMessageHandler(Dictionary<string, (HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string> Headers)> responsesByUrl)
            : this(request =>
            {
                if (!responsesByUrl.TryGetValue(request.RequestUri.ToString(), out var canned))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                var response = new HttpResponseMessage(canned.Status)
                {
                    Content = new StringContent(canned.Body ?? string.Empty)
                };

                if (canned.Headers != null)
                {
                    foreach (var (name, value) in canned.Headers)
                        response.Headers.TryAddWithoutValidation(name, value);
                }

                return response;
            })
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }

        public static HttpClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new(new FakeHttpMessageHandler(responder));
    }
}
