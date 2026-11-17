using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;

namespace AMAIS.WKFLO.AI.Application.Common
{
    public static class ErrorConst
    {
        // common
        public const string InvalidInput = "WKFLOAI0001";
        public const string Required = "WKFLOAI0002";
        public const string MinimumLength = "WKFLOAI0003";
        public const string MaximumLength = "WKFLOAI0004";
        public const string MustBePositive = "WKFLOAI0005";
        public const string MustBeInRangeMin1Max50 = "WKFLOAI0006";
        public const string MustBeInRangeMin1Max100 = "WKFLOAI0007";
        public const string MustBeInRangeMin1Max500 = "WKFLOAI0008";
        public const string MustBeInRangeMin1Max1000 = "WKFLOAI0009";
        public const string InvalidLanguage = "WKFLOAI0010";
        public const string InvalidEmail = "WKFLOAI0011";
        public const string InvalidPhone = "WKFLOAI0012";
        public const string InvalidCountry = "WKFLOAI0013";
        public const string AttachmentsTooLarge = "WKFLOAI0014";
        public const string ImageTooLarge = "WKFLOAI0015";
        public const string UnsupportedImageFormat = "WKFLOAI0016";

        // Tenant
        public const string TenantInvalid = "WKFLOAI0101";
        public const string TenantMustExist = "WKFLOAI0102";

        // Domain
        public const string DomainInvalid = "WKFLOAI0201";

        // Agent
        public const string AgentMustNotExist = "WKFLOAI0301";
        public const string AgentMustExist = "WKFLOAI0302";
        public const string AgentNotFound = "WKFLOAI0303";
        public const string AgentAndToolMustExist = "WKFLOAI0304";
        public const string AgentNotRunnable = "WKFLOAI0305";
        public const string AgentRoleMustBeUnique = "WKFLOAI0306";

        // Tool
        public const string ToolMustNotExist = "WKFLOAI0401";
        public const string ToolMustExist = "WKFLOAI0402";
        public const string ToolNotFound = "WKFLOAI0403";

        // AI Model
        public const string AIModelMustNotExist = "WKFLOAI0501";
        public const string AIModelMustExist = "WKFLOAI0502";
        public const string AIModelNotFound = "WKFLOAI0503";

        // Secret
        public const string SecretMustNotExist = "WKFLOAI0601";
        public const string SecretMustExist = "WKFLOAI0602";
        public const string SecretNotFound = "WKFLOAI0603";

        // Knowledge
        public const string KnowledgeMustNotExist = "WKFLOAI0701";
        public const string KnowledgeMustExist = "WKFLOAI0702";
        public const string KnowledgeNotFound = "WKFLOAI0703";

        // Tool MCP OAuth (public chat widget entry point)
        public const string ToolMcpOAuthNotConfigured = "WKFLOAI0801";
        public const string ToolMcpOAuthConsentFailed = "WKFLOAI0802";

        // MCP function discovery (WI 7447)
        public const string ToolMustBeMcp = "WKFLOAI0404";
        public const string ToolMcpOAuthNotConnected = "WKFLOAI0405";
        public const string ToolMcpUnavailable = "WKFLOAI0406";

        // Tool approvals (WI 7575)
        public const string ToolApprovalNotFound = "WKFLOAI0901";
        public const string ToolApprovalAlreadyDecided = "WKFLOAI0902";
        public const string ToolApprovalExpired = "WKFLOAI0903";
        public const string ToolApprovalDecisionInvalid = "WKFLOAI0904";

        // Chat Stop (US 7501)
        public const string ChatSessionNotOwned = "WKFLOAI1001";

        // Chat runtime (US 7959): a chat turn that failed after the send endpoint returned, pushed over SignalR. Defined in
        // Common.AI.Application (ChatErrorCodes) because the classifier and notifier live there.
        public const string ChatTurnFailed = ChatErrorCodes.ChatTurnFailed;
        public const string ChatModelTimeout = ChatErrorCodes.ChatModelTimeout;
        public const string ChatModelRateLimited = ChatErrorCodes.ChatModelRateLimited;
        public const string ChatModelQuotaExceeded = ChatErrorCodes.ChatModelQuotaExceeded;
        public const string ChatModelAuthFailed = ChatErrorCodes.ChatModelAuthFailed;
        public const string ChatModelContentFiltered = ChatErrorCodes.ChatModelContentFiltered;
        public const string ChatModelContextTooLong = ChatErrorCodes.ChatModelContextTooLong;
        public const string ChatModelUnavailable = ChatErrorCodes.ChatModelUnavailable;
        public const string ChatModelConfigInvalid = ChatErrorCodes.ChatModelConfigInvalid;
        public const string ChatModelEmptyResponse = ChatErrorCodes.ChatModelEmptyResponse;
        public const string ChatAgentUnavailable = ChatErrorCodes.ChatAgentUnavailable;
        public const string ChatToolUnavailable = ChatErrorCodes.ChatToolUnavailable;
        public const string ChatToolFailed = ChatErrorCodes.ChatToolFailed;
        public const string ChatAttachmentFailed = ChatErrorCodes.ChatAttachmentFailed;
        public const string ChatInfrastructureError = ChatErrorCodes.ChatInfrastructureError;
        public const string ChatSubagentFailed = ChatErrorCodes.ChatSubagentFailed;
        public const string ChatVoiceSynthesisFailed = ChatErrorCodes.ChatVoiceSynthesisFailed;
    }
}
