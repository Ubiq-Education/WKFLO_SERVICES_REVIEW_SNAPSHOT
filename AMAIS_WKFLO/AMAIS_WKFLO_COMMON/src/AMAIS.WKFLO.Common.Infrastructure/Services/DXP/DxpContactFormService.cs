using AMAIS.DXP.INGRESS.Client.Lib.Api;
using AMAIS.DXP.INGRESS.Client.Lib.Models;
using AMAIS.DXP.MD.Client.Lib.Api;
using AMAIS.WKFLO.Common.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Services.DXP
{
    public class DxpContactFormService : IDxpContactFormService
    {
        private readonly IInteractionApi _interactionApi;
        private readonly IContactApi _contactApi;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<DxpContactFormService> _logger;

        public DxpContactFormService(
            IInteractionApi interactionApi,
            IContactApi contactApi,
            IHttpContextAccessor httpContextAccessor,
            ILogger<DxpContactFormService> logger)
        {
            _interactionApi = interactionApi;
            _contactApi = contactApi;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<bool> ReportContactFormSubmissionAsync(
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
            string shareId)
        {
            if (_interactionApi == null || string.IsNullOrEmpty(subjectId))
            {
                _logger.LogDebug("DXP contact form reporting skipped — not configured or missing subject ID");
                return false;
            }

            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var language = httpContext?.Request.Headers["Accept-Language"].FirstOrDefault() ?? string.Empty;
                var ipAddress = GetRemoteIpAddress(httpContext);
                var userAgent = httpContext?.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;

                // ObjectMetadata.Metadata carries the agent identity (TenantAgentId/PublishedTenantAgentId/
                // ShareId) so DXP's "leads generated" KPI can filter contact-form submissions down to a
                // specific tenant agent — previously these ids were never sent at all, so that filter never
                // matched anything and the KPI was permanently stuck at zero.
                var objectMetadata = new CreateInteractionObjectMetadataCommand
                {
                    SiteName = dxpTenantName,
                    Metadata = new Dictionary<string, string>()
                };

                if (tenantAgentId.HasValue)
                    objectMetadata.Metadata["TenantAgentId"] = tenantAgentId.Value.ToString();

                if (publishedTenantAgentId.HasValue)
                    objectMetadata.Metadata["PublishedTenantAgentId"] = publishedTenantAgentId.Value.ToString();

                if (!string.IsNullOrWhiteSpace(shareId))
                    objectMetadata.Metadata["ShareId"] = shareId;

                var interaction = new CreateInteractionWithIPCommand
                {
                    Subject = subjectId,
                    // Reuses the predicate/ContactType the legacy in-house chatbot (ChatBotApi, pre-WKFlo)
                    // used for this exact purpose, so DXP-side reporting stays continuous across both
                    // chatbot generations instead of introducing a second, competing predicate for the
                    // same kind of event.
                    Predicate = "ChatBot Registration",
                    Object = sessionId,
                    Language = language,
                    Timestamp = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    SubjectMetadata = new Dictionary<string, string>(),
                    ObjectMetadata = objectMetadata
                };

                if (!string.IsNullOrWhiteSpace(firstName))
                    interaction.SubjectMetadata["FirstName"] = firstName;
                if (!string.IsNullOrWhiteSpace(lastName))
                    interaction.SubjectMetadata["LastName"] = lastName;
                if (!string.IsNullOrWhiteSpace(email))
                    interaction.SubjectMetadata["Email"] = email;

                interaction.SubjectMetadata["ContactType"] = "Chat";

                await _interactionApi.InteractionWithIPCreateAsync(interaction, dxpTenantName, domain, apiKey);

                _logger.LogInformation("DXP contact form submission reported for subject {SubjectId}, session {SessionId}", subjectId, sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DXP: Error reporting contact form submission for subject {SubjectId}", subjectId);
                return false;
            }
        }

        public async Task<bool> CheckContactExistsBySubjectIdAsync(Guid subjectId, string dxpTenantName)
        {
            if (_contactApi == null)
            {
                _logger.LogDebug("DXP contact check skipped — ContactApi not configured");
                return false;
            }

            try
            {
                var result = await _contactApi.ContactGetBySubjectIdAsync(subjectId, dxpTenantName);
                return result?.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DXP: Error checking contact existence for subject {SubjectId}", subjectId);
                return false;
            }
        }

        private static string GetRemoteIpAddress(HttpContext httpContext)
        {
            if (httpContext == null) return string.Empty;
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
                return forwardedFor.Split(',')[0].Trim();
            return httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
    }
}
