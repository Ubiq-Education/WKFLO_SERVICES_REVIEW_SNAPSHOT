namespace AMAIS.WKFLO.MCP.Application.Apis
{
    public abstract class ApiBase
    {

        protected readonly string _timeZone;

        protected ApiBase(string _timeZoneId)
        {
            _timeZone = _timeZoneId;
        }
    }
}
