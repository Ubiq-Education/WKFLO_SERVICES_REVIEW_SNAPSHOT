namespace AMAIS.WKFLO.MCP.Application.Common
{
    public static class ErrorConst
    {
        // common
        public const string InvalidInput = "WKFLOMCP0001";
        public const string Required = "WKFLOMCP0002";
        public const string MinimumLength = "WKFLOMCP0003";
        public const string MaximumLength = "WKFLOMCP0004";
        public const string MustBePositive = "WKFLOMCP0005";
        public const string MustBeInRangeMin1Max50 = "WKFLOMCP0006";
        public const string MustBeInRangeMin1Max100 = "WKFLOMCP0007";
        public const string MustBeInRangeMin1Max500 = "WKFLOMCP0008";
        public const string MustBeInRangeMin1Max1000 = "WKFLOMCP0009";
        public const string InvalidLanguage = "WKFLOMCP0010";
        public const string InvalidEmail = "WKFLOMCP0011";
        public const string InvalidPhone = "WKFLOMCP0012";
        public const string InvalidCountry = "WKFLOMCP0013";

        // Tenant
        public const string TenantInvalid = "WKFLOMCP0101";
        public const string TenantMustExist = "WKFLOMCP0102";

        // Domain
        public const string DomainInvalid = "WKFLOMCP0201";
    }
}
