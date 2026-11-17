namespace AMAIS.WKFLO.Common.Mediator
{
    public class MediatorWkfloMediator : IWkfloMediator
    {
        private readonly global::Mediator.IMediator _mediator;

        public MediatorWkfloMediator(global::Mediator.IMediator mediator) => _mediator = mediator;

        public async Task<TResponse> Send<TResponse>(IWkfloRequest<TResponse> request, CancellationToken cancellationToken = default)
            => await _mediator.Send(request, cancellationToken);

        public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : IWkfloNotification
            => await _mediator.Publish(notification, cancellationToken);
    }
}
