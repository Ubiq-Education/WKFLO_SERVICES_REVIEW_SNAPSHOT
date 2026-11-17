using AMAIS.WKFLO.Common.Mediator;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints
{
    [ApiController]
    public abstract class BaseSyncEndpoint<TRequest, TResponse> : BaseSyncEndpoint
    {
        protected BaseSyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }

        public abstract ActionResult<TResponse> Handle(TRequest request);
    }

    [ApiController]
    public abstract class BaseSyncEndpoint<TResponse> : BaseSyncEndpoint
    {
        protected BaseSyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }

        public abstract ActionResult<TResponse> Handle();
    }

    [ApiController]
    public abstract class BaseSyncEndpoint : BaseEndPoint
    {
        protected BaseSyncEndpoint(IWkfloMediator mediator) : base(mediator)
        {
        }
    }
}

