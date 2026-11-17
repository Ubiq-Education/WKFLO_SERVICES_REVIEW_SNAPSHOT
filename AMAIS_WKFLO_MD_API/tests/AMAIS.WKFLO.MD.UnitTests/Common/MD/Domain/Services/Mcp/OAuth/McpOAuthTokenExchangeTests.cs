using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth
{
    public class McpOAuthTokenExchangeTests
    {
        private const string TokenEndpoint = "https://auth.example.com/token";

        private static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"at","token_type":"Bearer","expires_in":3600,"refresh_token":"rt","scope":"read"}""")
        };

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_ClientSecretBasic_SendsBasicAuthHeader_NotInBody()
        {
            HttpRequestMessage captured = null;
            string capturedBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                captured = request;
                capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return SuccessResponse();
            });

            await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", "client-secret", McpOAuthTokenEndpointAuthMethods.ClientSecretBasic, "https://mcp.example.com/mcp",
                TestContext.Current.CancellationToken);

            Assert.Equal("Basic", captured.Headers.Authorization.Scheme);
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(captured.Headers.Authorization.Parameter));
            Assert.Equal("client-id:client-secret", decoded);
            Assert.DoesNotContain("client_secret", capturedBody);
        }

        // Copilot PR review (PR 16245): RFC 6749 §2.3.1 requires the client id and secret to each be
        // application/x-www-form-urlencoded BEFORE being joined with ':' and base64-encoded. Passing them
        // raw made the authorization server decode a different credential pair than the one it registered
        // (invalid_client) for any credential containing a reserved character — notably '+' and '/', both in
        // the standard base64 alphabet and so common in DCR-issued secrets, or a ':' inside the client id,
        // which would otherwise be read as the field separator itself.
        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_ClientSecretBasic_FormUrlEncodesReservedCharactersBeforeBase64()
        {
            HttpRequestMessage captured = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                captured = request;
                return SuccessResponse();
            });

            await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client:id", "se+cr/et=", McpOAuthTokenEndpointAuthMethods.ClientSecretBasic, "https://mcp.example.com/mcp",
                TestContext.Current.CancellationToken);

            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(captured.Headers.Authorization.Parameter));

            // Exactly one ':' survives as the separator — the one in the client id is percent-encoded, so the
            // server splits the credential in the right place and decodes both halves back to their originals.
            Assert.Equal("client%3Aid:se%2Bcr%2Fet%3D", decoded);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_ClientSecretPost_PutsClientIdAndSecretInBody_NoAuthHeader()
        {
            HttpRequestMessage captured = null;
            string capturedBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                captured = request;
                capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return SuccessResponse();
            });

            await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", "client-secret", McpOAuthTokenEndpointAuthMethods.ClientSecretPost, "https://mcp.example.com/mcp",
                TestContext.Current.CancellationToken);

            Assert.Null(captured.Headers.Authorization);
            Assert.Contains("client_id=client-id", capturedBody);
            Assert.Contains("client_secret=client-secret", capturedBody);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_None_PutsOnlyClientIdInBody()
        {
            HttpRequestMessage captured = null;
            string capturedBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                captured = request;
                capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return SuccessResponse();
            });

            await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", null, McpOAuthTokenEndpointAuthMethods.None, "https://mcp.example.com/mcp",
                TestContext.Current.CancellationToken);

            Assert.Null(captured.Headers.Authorization);
            Assert.Contains("client_id=client-id", capturedBody);
            Assert.DoesNotContain("client_secret", capturedBody);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_ResourceProvided_IncludedInBody()
        {
            string capturedBody = null;
            var handler = new FakeHttpMessageHandler(request =>
            {
                capturedBody = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return SuccessResponse();
            });

            await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", null, McpOAuthTokenEndpointAuthMethods.None, "https://mcp.example.com/mcp",
                TestContext.Current.CancellationToken);

            Assert.Contains(Uri.EscapeDataString("https://mcp.example.com/mcp"), capturedBody);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_Success_ReturnsParsedTokenResponse()
        {
            var handler = new FakeHttpMessageHandler(_ => SuccessResponse());

            var result = await McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", null, McpOAuthTokenEndpointAuthMethods.None, null, TestContext.Current.CancellationToken);

            Assert.Equal("at", result.AccessToken);
            Assert.Equal("Bearer", result.TokenType);
            Assert.Equal(3600, result.ExpiresIn);
            Assert.Equal("rt", result.RefreshToken);
            Assert.Equal("read", result.Scope);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_ErrorResponse_ThrowsWithErrorAndDescription()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"invalid_grant","error_description":"the code has expired"}""")
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", null, McpOAuthTokenEndpointAuthMethods.None, null, TestContext.Current.CancellationToken));

            Assert.Contains("invalid_grant", ex.Message);
            Assert.Contains("the code has expired", ex.Message);
        }

        [Fact]
        public async Task ExchangeAuthorizationCodeAsync_SuccessStatusButNoAccessToken_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => McpOAuthTokenExchange.ExchangeAuthorizationCodeAsync(
                new HttpClient(handler), TokenEndpoint, "auth-code", "https://app.example.com/callback", "verifier",
                "client-id", null, McpOAuthTokenEndpointAuthMethods.None, null, TestContext.Current.CancellationToken));
        }
    }
}
