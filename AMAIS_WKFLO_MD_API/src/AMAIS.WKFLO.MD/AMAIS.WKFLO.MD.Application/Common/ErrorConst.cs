namespace AMAIS.WKFLO.MD.Application.Common
{
    public static class ErrorConst
    {
        // common
        public const string InvalidInput = "WKFLOMD0001";
        public const string Required = "WKFLOMD0002";
        public const string MinimumLength = "WKFLOMD0003";
        public const string MaximumLength = "WKFLOMD0004";
        public const string MustBePositive = "WKFLOMD0005";
        public const string MustBeInRangeMin1Max50 = "WKFLOMD0006";
        public const string MustBeInRangeMin1Max100 = "WKFLOMD0007";
        public const string MustBeInRangeMin1Max500 = "WKFLOMD0008";
        public const string MustBeInRangeMin1Max1000 = "WKFLOMD0009";
        public const string InvalidLanguage = "WKFLOMD0010";
        public const string InvalidEmail = "WKFLOMD0011";
        public const string InvalidPhone = "WKFLOMD0012";
        public const string InvalidCountry = "WKFLOMD0013";

        // Tenant
        public const string TenantInvalid = "WKFLOMD0101";
        public const string TenantMustExist = "WKFLOMD0102";

        // Domain
        public const string DomainInvalid = "WKFLOMD0201";

        // Agent
        public const string AgentMustNotExist = "WKFLOMD0301";
        public const string AgentMustExist = "WKFLOMD0302";
        public const string AgentNotFound = "WKFLOMD0303";
        public const string AgentAndToolMustExist = "WKFLOMD0304";
        public const string AgentShareNotPublished = "WKFLOMD0305";
        public const string AgentShareNeedsRepublish = "WKFLOMD0306";
        public const string AgentRatingMustBeBetween1And5 = "WKFLOMD0307";

        // Tool
        public const string ToolMustNotExist = "WKFLOMD0401";
        public const string ToolMustExist = "WKFLOMD0402";
        public const string ToolNotFound = "WKFLOMD0403";
        public const string ToolShareNotPublished = "WKFLOMD0404";
        public const string ToolShareNeedsRepublish = "WKFLOMD0405";
        public const string ToolRatingMustBeBetween1And5 = "WKFLOMD0406";

        // Tool functions / permissions (WI 7447, WI 7575)
        public const string ToolMustBeMcp = "WKFLOMD0407";
        public const string ToolFunctionNotAvailable = "WKFLOMD0408";
        public const string ToolNotLinkedToAgent = "WKFLOMD0409";
        public const string ToolPermissionsNotEnabled = "WKFLOMD0410";
        public const string ToolPermissionInvalid = "WKFLOMD0411";
        public const string ToolFunctionsOnlyForMcp = "WKFLOMD0412";
        public const string ToolPermissionNotForMcp = "WKFLOMD0413";
        public const string ToolFunctionNotEnabled = "WKFLOMD0414";

        // AI Model
        public const string AIModelMustNotExist = "WKFLOMD0501";
        public const string AIModelMustExist = "WKFLOMD0502";
        public const string AIModelNotFound = "WKFLOMD0503";
        public const string AIModelAuthModeInvalidForProvider = "WKFLOMD0504";

        // Secret
        public const string SecretMustNotExist = "WKFLOMD0601";
        public const string SecretMustExist = "WKFLOMD0602";
        public const string SecretNotFound = "WKFLOMD0603";

        // Secret
        public const string KnowledgeMustNotExist = "WKFLOMD0701";
        public const string KnowledgeMustExist = "WKFLOMD0702";
        public const string KnowledgeNotFound = "WKFLOMD0703";

        // Tag
        public const string TagMustExist = "WKFLOMD0901";
        public const string TagNotFound = "WKFLOMD0902";
        public const string TagAlreadyAssigned = "WKFLOMD0903";
        public const string TagAssignmentNotFound = "WKFLOMD0904";
        public const string TagEntityTypeInvalid = "WKFLOMD0905";

        // Agent Allowed Spin Up Agent
        public const string AllowedSpinUpAgentMustExist = "WKFLOMD1001";
        public const string AllowedSpinUpAgentAlreadyAssigned = "WKFLOMD1002";
        public const string AllowedSpinUpAgentAssignmentNotFound = "WKFLOMD1003";

        // Agent Chat Setting
        public const string AgentChatSettingAlreadyExists = "WKFLOMD0801";
        public const string AgentChatSettingMustExist = "WKFLOMD0802";
        public const string AgentChatSettingNotFound = "WKFLOMD0803";
        public const string AgentChatSettingInvalidConfiguration = "WKFLOMD0804";
        public const string AgentChatSettingApiEndpointInvalid = "WKFLOMD08005";
        public const string AgentChatSettingShareIdMustBeUnique = "WKFLOMD0806";

        // Agent Chat Icon (image upload)
        public const string ImageDecodeFailed = "WKFLOMD1101";
        public const string ImageFormatInvalid = "WKFLOMD1102";

        // Tool MCP OAuth
        public const string ToolMcpOAuthNotConfigured = "WKFLOMD1201";
        public const string ToolMcpOAuthConsentFailed = "WKFLOMD1202";
        public const string ToolMcpOAuthNoPendingConsent = "WKFLOMD1203";
        public const string ToolMcpOAuthClientSecretRequiresClientId = "WKFLOMD1204";
        public const string ToolMcpConfigInvalid = "WKFLOMD1205";

        // Chat Classification Topic
        public const string ChatClassificationToolNotFound = "WKFLOMD1301";
        public const string ChatClassificationTopicNotFound = "WKFLOMD1302";

        // AI Credit
        public const string AICreditMustExist = "WKFLOMD1402";
        public const string AICreditNotFound = "WKFLOMD1403";
        public const string AICreditAlreadyExistsForAIModel = "WKFLOMD1404";
        public const string AIModelMustExistForAICredit = "WKFLOMD1405";
        public const string AICreditMultiplierMustBePositive = "WKFLOMD1406";
        public const string AICreditTokensMustBeNonNegative = "WKFLOMD1407";
        public const string AICreditCostMustBeNonNegative = "WKFLOMD1408";

        // Tenant Settings
        public const string TenantSettingTimeZoneInvalid = "WKFLOMD1501";
        public const string TenantSettingTimeFormatInvalid = "WKFLOMD1502";
        public const string TenantSettingDateFormatInvalid = "WKFLOMD1503";

        // Super Power Users / Tenant Access
        public const string SuperPowerUserRequired = "WKFLOMD1601";
        public const string CannotRemoveOwnSuperPowerUser = "WKFLOMD1602";
        public const string CannotRemoveLastSuperPowerUser = "WKFLOMD1603";
        public const string TenantAccessTenantNotFound = "WKFLOMD1604";

    }
}
