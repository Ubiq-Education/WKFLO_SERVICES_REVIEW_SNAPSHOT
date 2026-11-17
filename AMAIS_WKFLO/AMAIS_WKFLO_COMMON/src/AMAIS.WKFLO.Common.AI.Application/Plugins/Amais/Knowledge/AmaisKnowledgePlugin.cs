using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge
{
    public class AmaisKnowledgePlugin
    {
        public const string GetSchoolKnowledgeToolName = "get_school_knowledge";
        private readonly IEisKnowledgeProvider _eisKnowledgeProvider;
        private readonly IChatNotificationService _notificationService;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;

        public AmaisKnowledgePlugin(
            IEisKnowledgeProvider eisKnowledgeProvider,
            IChatNotificationService notificationService,
            TenantContext tenantContext,
            ChatContext chatContext)
        {
            _eisKnowledgeProvider = eisKnowledgeProvider;
            _notificationService = notificationService;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
        }

        [KernelFunction(GetSchoolKnowledgeToolName)]
        [Description("When the user asks a question, retrieve School knowledge if possible")]
        public async Task<string> GetSchoolKnowledgeAsync(
            [Description("User message")] string message,
            [Description("Current date and time in UTC, used as the 'now' reference point.")] DateTimeOffset? asOfUtc = null,
            [Description("Inclusive lower bound (UTC) an event's end date must fall on/after to be included, extracted from the user's time-context. Null means unbounded below.")] DateTimeOffset? eventDateRangeStartUtc = null,
            [Description("Inclusive upper bound (UTC) an event's end date must fall on/before to be included, extracted from the user's time-context. Null means unbounded above.")] DateTimeOffset? eventDateRangeEndUtc = null,
            [Description("True when the user asked for the earliest/next matching event, false for the latest/most recent, null for a general listing with no specific chronological extreme requested.")] bool? eventDateSortAscending = null)
        {
            try
            {
                var options = new EisKnowledgeQueryOptions
                {
                    BranchId = null,
                    AsOfUtc = asOfUtc,
                    EventDateRangeStartUtc = eventDateRangeStartUtc,
                    EventDateRangeEndUtc = eventDateRangeEndUtc,
                    EventDateSortAscending = eventDateSortAscending
                };

                var docs = await _eisKnowledgeProvider.RetrieveAsync(_tenantContext.TenantName, message, options);
                return BuildKnowledgeOutputText(docs, _chatContext.BasePath);
            }
            catch (Exception)
            {
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    "AmaisKnowledgePlugin",
                    "failed",
                    "Searching for school knowledge information failed...",
                    statusCode: 500
                );

                return string.Empty;
            }
        }

        public static string BuildKnowledgeOutputText(IReadOnlyList<EisDocument> docs, string baseUrl)
        {
            var sb = new StringBuilder();

            if (docs.Count > 0)
            {
                sb.AppendLine("Use the following knowledge base passages to answer the user's question:");
                sb.AppendLine();
                for (int i = 0; i < docs.Count; i++)
                {
                    sb.AppendLine($"---");
                    sb.AppendLine($"NAME: {docs[i].Name}");
                    sb.AppendLine($"PASSAGE: {docs[i].Content}");
                    sb.AppendLine($"LINKS: {ResolveRelativeLinks(docs[i].Links, baseUrl)}");
                    if (docs[i].EventEndDate.HasValue)
                    {
                        sb.AppendLine($"EVENT DATE: {docs[i].EventEndDate:yyyy-MM-dd}");
                    }
                    if (docs[i].ContentUpdatedAt.HasValue)
                    {
                        sb.AppendLine($"LAST UPDATED: {docs[i].ContentUpdatedAt:yyyy-MM-dd}");
                    }
                    sb.AppendLine($"---");
                }
                sb.AppendLine();
            }
            else
            {
                return "No relevant knowledge base passages were found to answer the user's question. Try to answer back using your own knowledge.";
            }

            sb.AppendLine("Keep your answers concise as they will be converted to speech.");

            return sb.ToString();
        }

        public static string ResolveRelativeLinks(string links, string baseUrl)
        {
            if (string.IsNullOrEmpty(links) || string.IsNullOrEmpty(baseUrl))
                return links;

            // Replace relative URLs (starting with /) inside the JSON "url" values
            return Regex.Replace(links, @"""url"":\s*""(/[^""]*?)""", m =>
            {
                var path = m.Groups[1].Value;
                return $@"""url"": ""{baseUrl.TrimEnd('/')}{path}""";
            });
        }
    }
}
