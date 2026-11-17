using Anthropic.Exceptions;
using Azure;
using Dapr;
using Dapr.Workflow;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.ClientModel;
using System.Data.Common;
using System.Net.Sockets;
using AnthropicErrorType = Anthropic.Models.ErrorType;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: maps an exception thrown while a chat turn runs to one of <see cref="ChatErrorCodes"/>. Pure and static:
    /// it only reads the exception (status codes, provider error bodies and messages) to pick a code; nothing it reads
    /// is ever returned. A user Stop is never an error: it returns null.
    /// </summary>
    public static class ChatErrorClassifier
    {
        /// <summary>How deep wrapped exceptions are followed (Aggregate / Kernel / inner exceptions).</summary>
        private const int MaxDepth = 8;

        private static readonly string[] QuotaMarkers =
            ["insufficient_quota", "exceeded your current quota", "billing", "credit balance is too low"];

        private static readonly string[] ContextTooLongMarkers =
            ["context_length_exceeded", "prompt is too long", "maximum context length", "context window", "too many tokens", "input is too long"];

        private static readonly string[] ContentFilterMarkers =
            ["content_filter", "content_policy_violation", "responsibleaipolicyviolation", "content management policy", "safety"];

        private static readonly string[] RateLimitMarkers = ["rate_limit", "rate limit", "too many requests"];

        private static readonly string[] UnavailableMarkers = ["overloaded", "service unavailable", "bad gateway"];

        private static readonly string[] TimeoutMarkers = ["timed out", "timeout"];

        private static readonly string[] ModelConfigMarkers =
            ["model_not_found", "deploymentnotfound", "deployment_not_found", "model not found", "unknown model", "invalid model",
             "unsupported model", "model is not supported", "does not exist", "operationnotsupported"];

        /// <summary>
        /// The code for <paramref name="exception"/>, or null when there is nothing to report: no exception, or the turn
        /// was stopped by the user (<paramref name="stopRequested"/>). Falls back to <see cref="ChatErrorCodes.ChatTurnFailed"/>.
        /// </summary>
        public static string Classify(Exception exception, bool stopRequested)
        {
            if (exception == null || stopRequested)
                return null;

            return ClassifyTree(exception, 0, new HashSet<Exception>(ReferenceEqualityComparer.Instance)) ?? ChatErrorCodes.ChatTurnFailed;
        }

        /// <summary>
        /// Maps an HTTP status and the (lowercased) provider error text to a code. Shared by every HTTP-carrying
        /// exception type. Never returns null: an unknown status falls back to <see cref="ChatErrorCodes.ChatTurnFailed"/>.
        /// </summary>
        public static string ClassifyHttp(int? status, string errorText)
        {
            var text = errorText?.ToLowerInvariant() ?? string.Empty;

            switch (status)
            {
                case 429:
                    return ContainsAny(text, QuotaMarkers) ? ChatErrorCodes.ChatModelQuotaExceeded : ChatErrorCodes.ChatModelRateLimited;
                case 402:
                    return ChatErrorCodes.ChatModelQuotaExceeded;
                case 401 or 403:
                    return ChatErrorCodes.ChatModelAuthFailed;
                case 408 or 504:
                    return ChatErrorCodes.ChatModelTimeout;
                case 413:
                    return ChatErrorCodes.ChatModelContextTooLong;
                case >= 500:
                    return ChatErrorCodes.ChatModelUnavailable;
            }

            if (ClassifyByMarkers(text) is { } byMarker)
                return byMarker;

            if (status == 404 || (status == 400 && ContainsAny(text, ModelConfigMarkers)))
                return ChatErrorCodes.ChatModelConfigInvalid;

            return ChatErrorCodes.ChatTurnFailed;
        }

        /// <summary>Depth-first, outermost first: the first exception that says something specific wins.</summary>
        private static string ClassifyTree(Exception exception, int depth, HashSet<Exception> visited)
        {
            if (exception == null || depth > MaxDepth || !visited.Add(exception))
                return null;

            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (ClassifyTree(inner, depth + 1, visited) is { } code)
                        return code;
                }

                return null;
            }

            return ClassifyNode(exception) ?? ClassifyTree(exception.InnerException, depth + 1, visited);
        }

        /// <summary>The code for this exception alone, or null when only its inner exception can tell.</summary>
        private static string ClassifyNode(Exception exception)
        {
            switch (exception)
            {
                // Model providers (HTTP).
                case HttpOperationException http:
                    return http.StatusCode is { } skStatus
                        ? ClassifyHttp((int)skStatus, Join(http.ResponseContent, http.Message))
                        : null; // No response: the inner exception (network, timeout) tells what happened.
                case ClientResultException clientResult:
                    return clientResult.Status > 0
                        ? ClassifyHttp(clientResult.Status, Join(ReadBody(clientResult), clientResult.Message))
                        : null;
                case AnthropicApiException anthropicApi:
                    return ClassifyHttp((int)anthropicApi.StatusCode, Join(anthropicApi.ResponseBody, anthropicApi.Message));
                case AnthropicIOException:
                    return ChatErrorCodes.ChatModelUnavailable;
                case AnthropicServiceException anthropicService:
                    // A streamed (SSE) error has no status, only the error type the API sent.
                    return ClassifyAnthropicErrorType(anthropicService.ErrorType, anthropicService.Message);
                case HttpRequestException httpRequest:
                    return httpRequest.StatusCode is { } requestStatus
                        ? ClassifyHttp((int)requestStatus, httpRequest.Message)
                        : ChatErrorCodes.ChatModelUnavailable;

                // Timeouts. The Stop case was already excluded, so a cancellation here is a timeout.
                case TimeoutException:
                case OperationCanceledException:
                    return ChatErrorCodes.ChatModelTimeout;

                // MCP servers.
                case McpException:
                case ClientTransportClosedException:
                    return ChatErrorCodes.ChatToolUnavailable;

                // Network without an HTTP response.
                case SocketException:
                    return ChatErrorCodes.ChatModelUnavailable;

                // Platform infrastructure.
                case DaprException:
                case RpcException:
                case DbException:
                case DbUpdateException:
                case RequestFailedException:
                    return ChatErrorCodes.ChatInfrastructureError;

                // A failed workflow activity crosses a process boundary: only the type name and the message survive.
                case WorkflowTaskFailedException workflowFailure:
                    return ClassifyWorkflowFailure(workflowFailure.FailureDetails);
            }

            return null;
        }

        private static string ClassifyAnthropicErrorType(AnthropicErrorType? errorType, string message)
        {
            var text = message?.ToLowerInvariant() ?? string.Empty;
            return errorType switch
            {
                AnthropicErrorType.RateLimitError => ChatErrorCodes.ChatModelRateLimited,
                AnthropicErrorType.BillingError => ChatErrorCodes.ChatModelQuotaExceeded,
                AnthropicErrorType.AuthenticationError or AnthropicErrorType.PermissionError => ChatErrorCodes.ChatModelAuthFailed,
                AnthropicErrorType.TimeoutError => ChatErrorCodes.ChatModelTimeout,
                AnthropicErrorType.OverloadedError or AnthropicErrorType.ApiError => ChatErrorCodes.ChatModelUnavailable,
                AnthropicErrorType.NotFoundError => ChatErrorCodes.ChatModelConfigInvalid,
                AnthropicErrorType.InvalidRequestError => ClassifyByMarkers(text) ?? ChatErrorCodes.ChatTurnFailed,
                _ => ClassifyByMarkers(text)
            };
        }

        private static string ClassifyWorkflowFailure(WorkflowTaskFailureDetails details)
        {
            if (details == null)
                return null;

            if (ClassifyByMarkers(details.ErrorMessage?.ToLowerInvariant() ?? string.Empty) is { } byMarker)
                return byMarker;

            var type = details.ErrorType ?? string.Empty;
            if (EndsWithTypeName(type, nameof(TimeoutException), nameof(TaskCanceledException), nameof(OperationCanceledException)))
                return ChatErrorCodes.ChatModelTimeout;
            if (type.Contains("Mcp", StringComparison.Ordinal))
                return ChatErrorCodes.ChatToolUnavailable;
            if (EndsWithTypeName(type, nameof(HttpRequestException), nameof(SocketException), nameof(AnthropicIOException)))
                return ChatErrorCodes.ChatModelUnavailable;
            if (EndsWithTypeName(type, nameof(DaprException), nameof(RpcException), "SqlException", "NpgsqlException", nameof(DbUpdateException), nameof(RequestFailedException)))
                return ChatErrorCodes.ChatInfrastructureError;

            return ChatErrorCodes.ChatTurnFailed;
        }

        /// <summary>Codes that can be told from the provider's error text alone, or null.</summary>
        private static string ClassifyByMarkers(string lowercaseText)
        {
            if (string.IsNullOrEmpty(lowercaseText))
                return null;
            if (ContainsAny(lowercaseText, QuotaMarkers))
                return ChatErrorCodes.ChatModelQuotaExceeded;
            if (ContainsAny(lowercaseText, ContextTooLongMarkers))
                return ChatErrorCodes.ChatModelContextTooLong;
            if (ContainsAny(lowercaseText, ContentFilterMarkers))
                return ChatErrorCodes.ChatModelContentFiltered;
            if (ContainsAny(lowercaseText, RateLimitMarkers))
                return ChatErrorCodes.ChatModelRateLimited;
            if (ContainsAny(lowercaseText, UnavailableMarkers))
                return ChatErrorCodes.ChatModelUnavailable;
            if (ContainsAny(lowercaseText, TimeoutMarkers))
                return ChatErrorCodes.ChatModelTimeout;
            return null;
        }

        private static string ReadBody(ClientResultException exception)
        {
            try
            {
                return exception.GetRawResponse()?.Content?.ToString();
            }
            catch (Exception)
            {
                // The response content was not buffered; the exception message still carries the provider's error code.
                return null;
            }
        }

        private static string Join(string first, string second) => $"{first} {second}";

        private static bool ContainsAny(string text, string[] markers) =>
            markers.Any(m => text.Contains(m, StringComparison.Ordinal));

        private static bool EndsWithTypeName(string fullTypeName, params string[] typeNames) =>
            typeNames.Any(n => fullTypeName == n || fullTypeName.EndsWith("." + n, StringComparison.Ordinal));
    }
}
