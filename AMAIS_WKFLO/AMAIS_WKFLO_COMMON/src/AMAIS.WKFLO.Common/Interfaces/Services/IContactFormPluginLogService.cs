namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    public interface IContactFormPluginLogService
    {
        Task<bool> ExistsAsync(Guid subjectId, string tenantName, CancellationToken cancellationToken = default);
    }
}
