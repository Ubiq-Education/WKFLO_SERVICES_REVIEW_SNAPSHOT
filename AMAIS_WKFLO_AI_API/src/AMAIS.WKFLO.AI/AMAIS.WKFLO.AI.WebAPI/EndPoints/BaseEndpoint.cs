using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints
{
    [ApiController]
    public abstract class BaseEndPoint : ControllerBase
    {
        protected IWkfloMediator Mediator { get; }

        protected BaseEndPoint(IWkfloMediator mediator)
        {
            Mediator = mediator;
        }

        protected virtual ObjectResult AmaisWkfloResult(Result result)
        {
            switch (result.ResultType)
            {
                case ResultType.Ok:
                    return Ok(result);
                case ResultType.Invalid:
                    return BadRequest(result);
                case ResultType.NotFound:
                    return NotFound(result);
                case ResultType.Unauthorized:
                    return Unauthorized(result);
                case ResultType.Conflict:
                    return Conflict(result);
                case ResultType.Gone:
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.Gone;
                    return new ObjectResult(result) { StatusCode = (int)HttpStatusCode.Gone };
                case ResultType.Unexpected:
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return new ObjectResult(result);
                default:
                    return BadRequest(result);
            }
        }
    }
}

