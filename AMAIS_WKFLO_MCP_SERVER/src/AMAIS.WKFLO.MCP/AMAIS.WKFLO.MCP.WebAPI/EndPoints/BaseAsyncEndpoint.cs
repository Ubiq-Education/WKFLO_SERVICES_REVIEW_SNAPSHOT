using AMAIS.WKFLO.Common.Mediator;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MCP.WebAPI.EndPoints
{
    [ApiController]
    public abstract class BaseAsyncEndpoint<TRequest, TResponse> : BaseAsyncEndpoint
    {
        protected BaseAsyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }

        public abstract Task<ActionResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
    }

    [ApiController]
    public abstract class BaseAsyncEndpoint<TRequest, TRequest2, TResponse> : BaseAsyncEndpoint
    {
        protected BaseAsyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }

        public abstract Task<ActionResult<TResponse>> HandleAsync(TRequest request, TRequest2 request2, CancellationToken cancellationToken = default);
    }

    [ApiController]
    public abstract class BaseAsyncEndpoint<TResponse> : BaseAsyncEndpoint
    {
        protected BaseAsyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }

        public abstract Task<ActionResult<TResponse>> HandleAsync(CancellationToken cancellationToken = default);
    }

    [ApiController]
    public abstract class BaseAsyncEndpoint : BaseEndPoint
    {
        protected BaseAsyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }
    }
}

