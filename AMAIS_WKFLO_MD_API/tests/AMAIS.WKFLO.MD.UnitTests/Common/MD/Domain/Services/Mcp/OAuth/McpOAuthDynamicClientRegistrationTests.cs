using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth
{
    public class McpOAuthDynamicClientRegistrationTests
    {
        private const string RegistrationEndpoint = "https://auth.example.com/register";

        [Fact]
        public async Task RegisterAsync_ServerReturnsClientIdAndSecretAndAuthMethod_ReturnsThemVerbatim()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                    {"client_id":"generated-client-id","client_secret":"generated-secret","token_endpoint_auth_method":"client_secret_basic"}
                    """)
            });

            var registration = await McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read write",
                TestContext.Current.CancellationToken);

            Assert.Equal("generated-client-id", registration.ClientId);
            Assert.Equal("generated-secret", registration.ClientSecret);
            Assert.Equal("client_secret_basic", registration.TokenEndpointAuthMethod);
        }

        [Fact]
        public async Task RegisterAsync_ServerOmitsAuthMethod_ButReturnsSecret_DefaultsToClientSecretPost()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"client_id":"cid","client_secret":"secret"}""")
            });

            var registration = await McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read",
                TestContext.Current.CancellationToken);

            Assert.Equal("client_secret_post", registration.TokenEndpointAuthMethod);
        }

        [Fact]
        public async Task RegisterAsync_ServerOmitsAuthMethodAndSecret_DefaultsToNone()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"client_id":"cid"}""")
            });

            var registration = await McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read",
                TestContext.Current.CancellationToken);

            Assert.Equal("none", registration.TokenEndpointAuthMethod);
        }

        [Fact]
        public async Task RegisterAsync_ServerRejectsRegistration_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_client_metadata"}""")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read",
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RegisterAsync_SuccessStatusButNoClientIdInBody_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read",
                TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RegisterAsync_SendsRedirectUriAndClientNameAndScopeInRequestBody()
        {
            HttpRequestMessage capturedRequest = null;
            string capturedBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                capturedRequest = request;
                capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"client_id":"cid"}""")
                };
            });

            await McpOAuthDynamicClientRegistration.RegisterAsync(
                new HttpClient(handler), RegistrationEndpoint, "https://app.example.com/callback", "My Tool", "read write",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            Assert.Contains("https://app.example.com/callback", capturedBody);
            Assert.Contains("My Tool", capturedBody);
            Assert.Contains("read write", capturedBody);
            Assert.Contains("authorization_code", capturedBody);
        }
    }
}
