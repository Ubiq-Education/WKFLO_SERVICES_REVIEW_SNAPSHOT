# Azure AD Authentication Configuration Fix

> **Doc drift note:** the two config changes this doc describes below are **not present in the codebase today** — neither service's `appsettings.Development.json` currently has an `AzureAd` section, and `Scopes` is still a plain string in MD API's `appsettings.json` (`"Scopes": "access_as_user"`) and absent entirely from AI API's. Whether this was reverted, superseded by different config elsewhere, or never actually landed isn't clear from the repo alone — verify current behavior before relying on this doc's "Solution"/"Files Modified" sections, which describe a past fix rather than the current state.

## Issue Description

The application was experiencing a `System.ArgumentNullException: Value cannot be null. (Parameter 'elementType')` error during startup. This error occurs when Microsoft Identity Web is trying to bind the Azure AD configuration, but the required configuration has an incompatible data type.

## Root Cause

The error was caused by two configuration issues:

1. **Missing `AzureAd` section** in `appsettings.Development.json` files
2. **Incorrect data type** for the `Scopes` property - it was configured as a string but Microsoft Identity Web expects an array

## Stack Trace Analysis

The error originated from:
```
System.Array.CreateInstance(Type elementType, Int32 length)
at Microsoft.Extensions.Configuration.ConfigurationBinder.BindInstance(...)
```

This indicates that the configuration binding process was trying to create an array but the `elementType` parameter was null, which happens when the configuration value type doesn't match the expected property type.

## Solution

### 1. Fixed Missing Configuration Sections

Added the required `AzureAd` configuration section to development configuration files:
- `AMAIS_WKFLO_MD_API\src\AMAIS.WKFLO.MD\AMAIS.WKFLO.MD.WebAPI\appsettings.Development.json`
- `AMAIS_WKFLO_AI_API\src\AMAIS.WKFLO.AI\AMAIS.WKFLO.AI.WebAPI\appsettings.Development.json`

### 2. Fixed Data Type Issues

Changed the `Scopes` property from string to array format in base configuration files:

**Before (Incorrect):**
```json
{
  "AzureAd": {
    "Scopes": "access_as_user"
  }
}
```

**After (Correct):**
```json
{
  "AzureAd": {
    "Scopes": [ "access_as_user" ]
  }
}
```

## Complete Configuration Example

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "your-domain.com",
    "TenantId": "your-tenant-id-here",
    "ClientId": "your-client-id-here",
    "CallbackPath": "/signin-oidc",
    "Scopes": [ "access_as_user" ],
    "ClientSecret": "your-client-secret",
    "ClientCertificates": []
  }
}
```

## Key Configuration Points

1. **Scopes**: Must be an array `[ "scope1", "scope2" ]`, not a string `"scope1"`
2. **ClientCertificates**: Must be an array `[]`, even if empty
3. **TenantId and ClientId**: Should be valid GUIDs
4. **Instance**: Typically `"https://login.microsoftonline.com/"` for Azure AD

## Files Modified

- `AMAIS_WKFLO_MD_API\src\AMAIS.WKFLO.MD\AMAIS.WKFLO.MD.WebAPI\appsettings.json`
- `AMAIS_WKFLO_MD_API\src\AMAIS.WKFLO.MD\AMAIS.WKFLO.MD.WebAPI\appsettings.Development.json`
- `AMAIS_WKFLO_AI_API\src\AMAIS.WKFLO.AI\AMAIS.WKFLO.AI.WebAPI\appsettings.json`
- `AMAIS_WKFLO_AI_API\src\AMAIS.WKFLO.AI\AMAIS.WKFLO.AI.WebAPI\appsettings.Development.json`

## Verification

After applying these fixes, the application should start without the `System.ArgumentNullException` error. You can verify this by:

1. Stopping the application
2. Rebuilding the solution
3. Starting the application again
4. Checking that authentication endpoints respond correctly

## Environment Variables (Alternative)

For production or containerized environments, you can use environment variables:

```bash
# Windows
set AzureAd__TenantId=your-tenant-id-here
set AzureAd__ClientId=your-client-id-here
set AzureAd__Scopes__0=access_as_user

# Linux/Mac
export AzureAd__TenantId=your-tenant-id-here
export AzureAd__ClientId=your-client-id-here
export AzureAd__Scopes__0=access_as_user
```

Note: For arrays in environment variables, use the indexed format `__0`, `__1`, etc.

## Production Considerations

- Never commit real Azure AD credentials to source control
- Use Azure Key Vault for production secrets
- Consider using Managed Identity in Azure environments
- Validate that all array properties use correct JSON array syntax
- Test configuration binding in different environments

## Related Microsoft Documentation

- [Microsoft Identity Web Configuration](https://docs.microsoft.com/en-us/azure/active-directory/develop/microsoft-identity-web)
- [ASP.NET Core Configuration](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Azure AD App Registration](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)