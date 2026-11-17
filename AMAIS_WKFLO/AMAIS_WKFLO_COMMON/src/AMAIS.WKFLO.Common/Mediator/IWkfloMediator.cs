namespace AMAIS.WKFLO.Common.Mediator
{
    public interface IWkfloMediator
    {
        Task<TResponse> Send<TResponse>(IWkfloRequest<TResponse> request, CancellationToken cancellationToken = default);
        Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : IWkfloNotification;
    }
}
