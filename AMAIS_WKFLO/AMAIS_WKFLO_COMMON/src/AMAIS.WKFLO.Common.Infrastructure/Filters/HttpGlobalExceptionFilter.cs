using AMAIS.WKFLO.Common.Models.Results;
using Flurl.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using static AMAIS.WKFLO.Common.Models.CommonConsts;
using NotFoundResult = AMAIS.WKFLO.Common.Models.Results.NotFoundResult;
using UnauthorizedResult = AMAIS.WKFLO.Common.Models.Results.UnauthorizedResult;

namespace AMAIS.WKFLO.Common.Infrastructure.Filters
{
    public class HttpGlobalExceptionFilter(ILogger<HttpGlobalExceptionFilter> logger) : IExceptionFilter
    {
        private readonly ILogger<HttpGlobalExceptionFilter> _logger = logger;

        public void OnException(ExceptionContext context)
        {
            _logger.LogError(context.Exception, "HttpGlobalExceptionFilter Error: {message}", context.Exception.Message);

            if (context.Exception is JsonSerializationException serializationException)
            {
                var reponse = new InvalidResult();
                reponse.AddError(serializationException.Path ?? string.Empty, CommonErrorConst.InvalidInput);
                context.Result = new ObjectResult(reponse);
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
            else if (context.Exception is FlurlHttpException ex)
            {
                context.Result = WKflowResult(ex.Call);
                context.HttpContext.Response.StatusCode = ex.Call?.Response?.StatusCode ?? (int)HttpStatusCode.InternalServerError;
            }
            else
            {
                var reponse = new UnexpectedResult();
                reponse.AddError(string.Empty, CommonErrorConst.UnexpectedResult);
                context.Result = new ObjectResult(reponse);
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

            context.ExceptionHandled = true;
        }

        protected static ObjectResult WKflowResult(FlurlCall call)
        {
            if (call?.HttpResponseMessage == null)
            {
                return new ObjectResult(new UnexpectedResult());
            }

            var content = call.HttpResponseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return call.Response.StatusCode switch
            {
                (int)HttpStatusCode.OK => new ObjectResult(JsonConvert.DeserializeObject<SuccessResult<dynamic>>(content)),
                (int)HttpStatusCode.BadRequest => new ObjectResult(JsonConvert.DeserializeObject<InvalidResult>(content)),
                (int)HttpStatusCode.NotFound => new ObjectResult(JsonConvert.DeserializeObject<NotFoundResult>(content)),
                (int)HttpStatusCode.Unauthorized => new ObjectResult(JsonConvert.DeserializeObject<UnauthorizedResult>(content)),
                (int)HttpStatusCode.InternalServerError => new ObjectResult(JsonConvert.DeserializeObject<UnexpectedResult>(content)),
                _ => new ObjectResult(new UnexpectedResult()),
            };
        }
    }
}
