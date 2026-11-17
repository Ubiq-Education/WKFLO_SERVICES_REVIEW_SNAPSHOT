using AMAIS.WKFLO.Common.Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted
{
    public class ContactFormSubmittedDomainEvent : IWkfloNotification
    {
        public string TenantName { get; }
        public string SubjectId { get; }
        public string SessionId { get; }
        public string FirstName { get; }
        public string LastName { get; }
        public string Email { get; }
        public string ShareId { get; }
        public int? AgentId { get; }

        public ContactFormSubmittedDomainEvent(
            string tenantName,
            string subjectId,
            string sessionId,
            string firstName,
            string lastName,
            string email,
            string shareId = null,
            int? agentId = null)
        {
            TenantName = tenantName;
            SubjectId = subjectId;
            SessionId = sessionId;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            ShareId = shareId;
            AgentId = agentId;
        }
    }
}
