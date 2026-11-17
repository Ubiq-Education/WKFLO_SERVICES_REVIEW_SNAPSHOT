namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.Authorization
{
    /// <summary>
    /// Contains authorization policy names used in the application.
    /// </summary>
    public static class AuthorizationPolicies
    {
        /// <summary>
        /// Policy that requires either 'access_as_user' scope (delegated) or 'Api.Access' app role (application).
        /// </summary>
        public const string ApiAccess = "ApiAccess";
    }

    /// <summary>
    /// Contains scope names for delegated permissions.
    /// </summary>
    public static class Scopes
    {
        public const string AccessAsUser = "access_as_user";
    }

    /// <summary>
    /// Contains app role names for application permissions.
    /// </summary>
    public static class AppRoles
    {
        public const string ApiAccess = "Api.Access";
    }
}
