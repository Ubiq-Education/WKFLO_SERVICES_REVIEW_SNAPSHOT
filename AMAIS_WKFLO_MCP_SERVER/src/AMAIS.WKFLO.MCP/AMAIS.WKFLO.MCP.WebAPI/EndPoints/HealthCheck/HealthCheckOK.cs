using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MCP.WebAPI.EndPoints.HealthCheck
{
    [ApiController]
    public class HealthCheckOK : ControllerBase
    {
        public HealthCheckOK()
        {
        }

        [HttpGet("/api/v1/healthcheck")]

        public ActionResult HandleAsync()
        {
            return Ok();
        }
    }
}
