namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    public interface IDxpContactFormService
    {
        Task<bool> ReportContactFormSubmissionAsync(
            string dxpTenantName,
            string domain,
            string apiKey,
            string subjectId,
            string sessionId,
            string firstName,
            string lastName,
            string email,
            int? tenantAgentId,
            int? publishedTenantAgentId,
            string shareId);

        Task<bool> CheckContactExistsBySubjectIdAsync(Guid subjectId, string dxpTenantName);
    }
}
