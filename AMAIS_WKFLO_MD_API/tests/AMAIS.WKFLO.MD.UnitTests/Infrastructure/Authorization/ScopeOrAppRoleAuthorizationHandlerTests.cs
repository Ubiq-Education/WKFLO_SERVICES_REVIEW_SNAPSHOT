using AMAIS.WKFLO.MD.WebAPI.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Authorization
{
    /// <summary>
    /// Unit tests for ScopeOrAppRoleAuthorizationHandler to verify it correctly validates
    /// both user scopes and application roles.
    /// </summary>
    public class ScopeOrAppRoleAuthorizationHandlerTests
    {
        [Fact]
        public async Task HandleRequirementAsync_WithValidUserScope_Succeeds()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/identity/claims/scope", "access_as_user")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithValidAppRole_Succeeds()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "Api.Access")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithValidRolesClaimAlternative_Succeeds()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("roles", "Api.Access")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithMultipleScopes_Succeeds()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/identity/claims/scope", "access_as_user other_scope")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithMultipleRoles_Succeeds()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "Other.Role"),
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "Api.Access")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithInvalidScope_Fails()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/identity/claims/scope", "invalid_scope")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithInvalidRole_Fails()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "Invalid.Role")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithNoClaims_Fails()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: new[] { "access_as_user" },
                acceptedRoles: new[] { "Api.Access" }
            );

            var claims = new List<Claim>();
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_WithEmptyRequirement_Fails()
        {
            // Arrange
            var handler = new ScopeOrAppRoleAuthorizationHandler();
            var requirement = new ScopeOrAppRoleRequirement(
                acceptedScopes: Array.Empty<string>(),
                acceptedRoles: Array.Empty<string>()
            );

            var claims = new List<Claim>
            {
                new Claim("http://schemas.microsoft.com/identity/claims/scope", "access_as_user")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var user = new ClaimsPrincipal(identity);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            // Act
            await handler.HandleAsync(context);

            // Assert
            Assert.False(context.HasSucceeded);
        }
    }
}
