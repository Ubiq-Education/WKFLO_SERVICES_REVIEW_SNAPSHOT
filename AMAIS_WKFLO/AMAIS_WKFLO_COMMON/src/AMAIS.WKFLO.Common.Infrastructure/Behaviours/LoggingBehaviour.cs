using AMAIS.WKFLO.Common.MD.Domain.Security;
using global::Mediator;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.Infrastructure.Behaviours
{
    public class LoggingBehaviour<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, IMessage
    {
        private readonly ILogger _logger;

        public LoggingBehaviour(ILogger<TMessage> logger)
        {
            _logger = logger;
        }

        public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            var requestName = typeof(TMessage).Name;
            // US 7849: requests can carry OAuth secrets — a plaintext client secret inside a tool's Config
            // (encrypted only later, in the handler), or an authorization code/state on the consent callback.
            // Never let them reach the logs. Bug 8189: file attachments ([OmitFromLogs]) are left out too.
            _logger.LogInformation("Handling request {requestName}, data: {data}", requestName,
                SensitiveDataRedactor.RedactFor(message, JsonConvert.SerializeObject(message, LogJsonSettings.Default)));
            var response = await next(message, cancellationToken);
            _logger.LogInformation("Handled request: {requestName}", requestName);

            return response;
        }
    }
}