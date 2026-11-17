using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using Anthropic.Exceptions;
using Azure;
using Dapr;
using Dapr.Workflow;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using ModelContextProtocol;
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AnthropicErrorType = Anthropic.Models.ErrorType;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>US 7959: exceptions of a chat turn map to a WKFLOAI11xx code; a user Stop is never an error.</summary>
    public class ChatErrorClassifierTests
    {
        private static HttpOperationException SkHttp(HttpStatusCode? status, string body = null) =>
            new(status, body, "Service request failed.", null);

        private static AnthropicApiException AnthropicHttp(HttpStatusCode status, string body = "") =>
            new AnthropicUnexpectedStatusCodeException(new HttpRequestException()) { StatusCode = status, ResponseBody = body };

        private static ClientResultException ClientResult(int status, string body = null, string message = "HTTP error") =>
            new(message, new FakePipelineResponse(status, body));

        [Fact]
        public void Classify_NoException_ReturnsNull()
        {
            Assert.Null(ChatErrorClassifier.Classify(null, stopRequested: false));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Classify_StopRequested_ReturnsNullWhateverTheException(bool cancellation)
        {
            Exception exception = cancellation ? new OperationCanceledException() : SkHttp(HttpStatusCode.InternalServerError);

            Assert.Null(ChatErrorClassifier.Classify(exception, stopRequested: true));
        }

        [Theory]
        [InlineData(408, null, ChatErrorCodes.ChatModelTimeout)]
        [InlineData(504, null, ChatErrorCodes.ChatModelTimeout)]
        [InlineData(429, "{\"error\":{\"code\":\"rate_limit_exceeded\"}}", ChatErrorCodes.ChatModelRateLimited)]
        [InlineData(429, "{\"error\":{\"code\":\"insufficient_quota\"}}", ChatErrorCodes.ChatModelQuotaExceeded)]
        [InlineData(402, null, ChatErrorCodes.ChatModelQuotaExceeded)]
        [InlineData(401, null, ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(403, null, ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(400, "{\"error\":{\"code\":\"content_filter\"}}", ChatErrorCodes.ChatModelContentFiltered)]
        [InlineData(400, "{\"error\":{\"innererror\":{\"code\":\"ResponsibleAIPolicyViolation\"}}}", ChatErrorCodes.ChatModelContentFiltered)]
        [InlineData(400, "{\"error\":{\"code\":\"context_length_exceeded\"}}", ChatErrorCodes.ChatModelContextTooLong)]
        [InlineData(400, "prompt is too long: 210000 tokens > 200000 maximum", ChatErrorCodes.ChatModelContextTooLong)]
        [InlineData(413, null, ChatErrorCodes.ChatModelContextTooLong)]
        [InlineData(500, null, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(502, null, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(503, null, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}", ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(404, null, ChatErrorCodes.ChatModelConfigInvalid)]
        [InlineData(400, "{\"error\":{\"code\":\"DeploymentNotFound\"}}", ChatErrorCodes.ChatModelConfigInvalid)]
        [InlineData(400, "{\"error\":{\"code\":\"model_not_found\"}}", ChatErrorCodes.ChatModelConfigInvalid)]
        [InlineData(400, "{\"error\":{\"code\":\"invalid_value\"}}", ChatErrorCodes.ChatTurnFailed)]
        [InlineData(422, null, ChatErrorCodes.ChatTurnFailed)]
        public void Classify_SemanticKernelHttpOperation_MapsStatusAndBody(int status, string body, string expected)
        {
            Assert.Equal(expected, ChatErrorClassifier.Classify(SkHttp((HttpStatusCode)status, body), stopRequested: false));
        }

        [Theory]
        [InlineData(429, "{\"error\":{\"type\":\"rate_limit_error\"}}", ChatErrorCodes.ChatModelRateLimited)]
        [InlineData(401, "{\"error\":{\"type\":\"authentication_error\"}}", ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(400, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long\"}}", ChatErrorCodes.ChatModelContextTooLong)]
        [InlineData(400, "{\"error\":{\"message\":\"Your credit balance is too low\"}}", ChatErrorCodes.ChatModelQuotaExceeded)]
        [InlineData(529, "{\"error\":{\"type\":\"overloaded_error\"}}", ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(404, "{\"error\":{\"type\":\"not_found_error\"}}", ChatErrorCodes.ChatModelConfigInvalid)]
        public void Classify_AnthropicApiException_MapsStatusAndBody(int status, string body, string expected)
        {
            Assert.Equal(expected, ChatErrorClassifier.Classify(AnthropicHttp((HttpStatusCode)status, body), stopRequested: false));
        }

        [Theory]
        [InlineData(AnthropicErrorType.RateLimitError, ChatErrorCodes.ChatModelRateLimited)]
        [InlineData(AnthropicErrorType.OverloadedError, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(AnthropicErrorType.ApiError, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(AnthropicErrorType.TimeoutError, ChatErrorCodes.ChatModelTimeout)]
        [InlineData(AnthropicErrorType.AuthenticationError, ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(AnthropicErrorType.PermissionError, ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(AnthropicErrorType.BillingError, ChatErrorCodes.ChatModelQuotaExceeded)]
        [InlineData(AnthropicErrorType.NotFoundError, ChatErrorCodes.ChatModelConfigInvalid)]
        [InlineData(AnthropicErrorType.InvalidRequestError, ChatErrorCodes.ChatTurnFailed)]
        public void Classify_AnthropicStreamedError_MapsErrorType(AnthropicErrorType errorType, string expected)
        {
            var exception = new AnthropicSseException("stream error", null) { ErrorType = errorType };

            Assert.Equal(expected, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_AnthropicIOException_IsUnavailable()
        {
            var exception = new AnthropicIOException("I/O error", new HttpRequestException("No such host is known."));

            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Theory]
        [InlineData(429, "{\"error\":{\"code\":\"insufficient_quota\"}}", ChatErrorCodes.ChatModelQuotaExceeded)]
        [InlineData(400, "{\"error\":{\"code\":\"content_filter\"}}", ChatErrorCodes.ChatModelContentFiltered)]
        [InlineData(503, null, ChatErrorCodes.ChatModelUnavailable)]
        [InlineData(404, null, ChatErrorCodes.ChatModelConfigInvalid)]
        public void Classify_ClientResultException_MapsStatusAndBody(int status, string body, string expected)
        {
            Assert.Equal(expected, ChatErrorClassifier.Classify(ClientResult(status, body), stopRequested: false));
        }

        [Fact]
        public void Classify_ClientResultException_ReadsMarkersFromTheMessageWhenTheBodyIsNotBuffered()
        {
            var exception = ClientResult(400, body: null, message: "HTTP 400 (invalid_request_error: context_length_exceeded)");

            Assert.Equal(ChatErrorCodes.ChatModelContextTooLong, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, ChatErrorCodes.ChatModelRateLimited)]
        [InlineData(HttpStatusCode.Unauthorized, ChatErrorCodes.ChatModelAuthFailed)]
        [InlineData(HttpStatusCode.BadGateway, ChatErrorCodes.ChatModelUnavailable)]
        public void Classify_HttpRequestExceptionWithStatus_MapsStatus(HttpStatusCode status, string expected)
        {
            var exception = new HttpRequestException("Response status code does not indicate success.", null, status);

            Assert.Equal(expected, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_HttpRequestExceptionWithoutStatus_IsUnavailable()
        {
            Assert.Equal(ChatErrorCodes.ChatModelUnavailable,
                ChatErrorClassifier.Classify(new HttpRequestException("No such host is known."), stopRequested: false));
        }

        [Fact]
        public void Classify_SkHttpOperationWithoutStatus_UsesTheInnerException()
        {
            var exception = new HttpOperationException(null, null, "Request failed", new SocketException((int)SocketError.ConnectionRefused));

            Assert.Equal(ChatErrorCodes.ChatModelUnavailable, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_ClientResultWithoutResponse_UsesTheInnerException()
        {
            var exception = new ClientResultException("Network timeout", null, new TaskCanceledException());

            Assert.Equal(ChatErrorCodes.ChatModelTimeout, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_TimeoutException_IsTimeout()
        {
            Assert.Equal(ChatErrorCodes.ChatModelTimeout, ChatErrorClassifier.Classify(new TimeoutException(), stopRequested: false));
        }

        [Fact]
        public void Classify_TaskCanceledWithoutStop_IsTimeout()
        {
            Assert.Equal(ChatErrorCodes.ChatModelTimeout, ChatErrorClassifier.Classify(new TaskCanceledException(), stopRequested: false));
        }

        [Fact]
        public void Classify_OperationCanceledWithoutStop_IsTimeout()
        {
            Assert.Equal(ChatErrorCodes.ChatModelTimeout,
                ChatErrorClassifier.Classify(new OperationCanceledException(new CancellationToken(true)), stopRequested: false));
        }

        [Fact]
        public void Classify_McpException_IsToolUnavailable()
        {
            Assert.Equal(ChatErrorCodes.ChatToolUnavailable, ChatErrorClassifier.Classify(new McpException("connect failed"), stopRequested: false));
        }

        [Fact]
        public void Classify_DaprException_IsInfrastructure()
        {
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError,
                ChatErrorClassifier.Classify(new DaprException("state store unavailable"), stopRequested: false));
        }

        [Fact]
        public void Classify_RpcException_IsInfrastructure()
        {
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError,
                ChatErrorClassifier.Classify(new RpcException(new Status(StatusCode.Unavailable, "sidecar down")), stopRequested: false));
        }

        [Fact]
        public void Classify_DbUpdateException_IsInfrastructure()
        {
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError,
                ChatErrorClassifier.Classify(new DbUpdateException("save failed"), stopRequested: false));
        }

        [Fact]
        public void Classify_RequestFailedException_IsInfrastructure()
        {
            Assert.Equal(ChatErrorCodes.ChatInfrastructureError,
                ChatErrorClassifier.Classify(new RequestFailedException(503, "blob unavailable"), stopRequested: false));
        }

        [Fact]
        public void Classify_UnknownException_FallsBackToTurnFailed()
        {
            Assert.Equal(ChatErrorCodes.ChatTurnFailed,
                ChatErrorClassifier.Classify(new InvalidOperationException("boom"), stopRequested: false));
        }

        [Fact]
        public void Classify_WrappedInAggregateAndKernelExceptions_UnwrapsToTheProviderError()
        {
            var exception = new AggregateException(
                new InvalidOperationException("first, unknown"),
                new KernelException("kernel failed", new InvalidOperationException("wrapper", SkHttp(HttpStatusCode.TooManyRequests))));

            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_OutermostSpecificExceptionWins()
        {
            // An Anthropic API error wraps the HttpRequestException it came from: the status on the outer one decides.
            var exception = AnthropicHttp(HttpStatusCode.TooManyRequests);

            Assert.Equal(ChatErrorCodes.ChatModelRateLimited, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Theory]
        [InlineData("System.TimeoutException", "The operation has timed out.", ChatErrorCodes.ChatModelTimeout)]
        [InlineData("Microsoft.SemanticKernel.HttpOperationException", "Response status code does not indicate success: 429 (Too Many Requests).", ChatErrorCodes.ChatModelRateLimited)]
        [InlineData("ModelContextProtocol.McpException", "Failed to connect", ChatErrorCodes.ChatToolUnavailable)]
        [InlineData("System.Net.Http.HttpRequestException", "No such host is known.", ChatErrorCodes.ChatModelUnavailable)]
        [InlineData("Dapr.DaprException", "State operation failed", ChatErrorCodes.ChatInfrastructureError)]
        [InlineData("Microsoft.Data.SqlClient.SqlException", "Login failed", ChatErrorCodes.ChatInfrastructureError)]
        [InlineData("System.InvalidOperationException", "boom", ChatErrorCodes.ChatTurnFailed)]
        public void Classify_WorkflowTaskFailed_UsesTheReportedTypeAndMessage(string errorType, string errorMessage, string expected)
        {
            var exception = new WorkflowTaskFailedException("Activity failed", new WorkflowTaskFailureDetails(errorType, errorMessage, null));

            Assert.Equal(expected, ChatErrorClassifier.Classify(exception, stopRequested: false));
        }

        [Fact]
        public void Classify_CircularInnerExceptionsDoNotLoop()
        {
            var aggregate = new AggregateException(new InvalidOperationException("a"));
            var outer = new AggregateException(aggregate, aggregate);

            Assert.Equal(ChatErrorCodes.ChatTurnFailed, ChatErrorClassifier.Classify(outer, stopRequested: false));
        }

        [Fact]
        public void ClassifyHttp_NullStatusAndNoMarkers_FallsBackToTurnFailed()
        {
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, ChatErrorClassifier.ClassifyHttp(null, null));
        }

        private sealed class FakePipelineResponse : PipelineResponse
        {
            private readonly BinaryData _content;

            public FakePipelineResponse(int status, string body)
            {
                Status = status;
                _content = body == null ? null : BinaryData.FromString(body);
            }

            public override int Status { get; }
            public override string ReasonPhrase => string.Empty;
            public override Stream ContentStream { get; set; }

            // Unbuffered content throws, like the real responses do.
            public override BinaryData Content => _content ?? throw new InvalidOperationException("The response content was not buffered.");

            protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
            public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
            public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);
            public override void Dispose() { }
        }
    }
}
