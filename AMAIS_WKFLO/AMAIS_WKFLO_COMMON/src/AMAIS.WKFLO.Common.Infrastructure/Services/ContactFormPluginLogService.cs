using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Services
{
    public class ContactFormPluginLogService : IContactFormPluginLogService
    {
        private readonly IMasterDataDbContext _db;
        private readonly ILogger<ContactFormPluginLogService> _logger;

        public ContactFormPluginLogService(IMasterDataDbContext db, ILogger<ContactFormPluginLogService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<bool> ExistsAsync(Guid subjectId, string tenantName, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _db.ContactFormPluginLogs
                    .AsNoTracking()
                    .AnyAsync(l => l.SubjectId == subjectId.ToString()
                                   && l.Tenant.Name == tenantName
                                   && !l.IsDeleted, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking ContactFormPluginLog for subject {SubjectId}", subjectId);
                return false;
            }
        }
    }
}
