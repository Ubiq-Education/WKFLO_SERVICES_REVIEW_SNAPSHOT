using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.Helpers
{
    public class JwtBearerEventsHelperTests
    {
        private static (ChatContext chatContext, IServiceProvider services) BuildServices()
        {
            var chatContext = new ChatContext();
            var services = new ServiceCollection();
            services.AddSingleton(chatContext);
            return (chatContext, services.BuildServiceProvider());
        }

        private static JsonWebToken BuildJwt(string aud = null)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, "user-1"),
                new(JwtRegisteredClaimNames.Iss, "https://issuer.example.com"),
            };

            if (aud != null)
                claims.Add(new Claim(JwtRegisteredClaimNames.Aud, aud));

            var handler = new JsonWebTokenHandler();
            var tokenString = handler.CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes("test-signing-key-32-bytes-minimum!")),
                    SecurityAlgorithms.HmacSha256)
            });

            return handler.ReadJsonWebToken(tokenString);
        }

        [Fact]
        public void ApplyAudienceToBasePath_WithAudClaim_SetsBasePath()
        {
            var (chatContext, services) = BuildServices();
            var jwt = BuildJwt(aud: "https://tenant.example.com");

            JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, services);

            Assert.Equal("https://tenant.example.com", chatContext.BasePath);
        }

        [Fact]
        public void ApplyAudienceToBasePath_WithoutAudClaim_LeavesBasePathNull()
        {
            var (chatContext, services) = BuildServices();
            var jwt = BuildJwt(aud: null);

            JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, services);

            Assert.Null(chatContext.BasePath);
        }

        [Fact]
        public void ApplyAudienceToBasePath_WithEmptyAudClaim_LeavesBasePathNull()
        {
            var (chatContext, services) = BuildServices();
            var jwt = BuildJwt(aud: "");

            JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, services);

            Assert.Null(chatContext.BasePath);
        }

        [Fact]
        public void ApplyAudienceToBasePath_WithWhitespaceAudClaim_LeavesBasePathNull()
        {
            var (chatContext, services) = BuildServices();
            var jwt = BuildJwt(aud: "   ");

            JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, services);

            Assert.Null(chatContext.BasePath);
        }

        [Fact]
        public void ApplyAudienceToBasePath_DoesNotOverwriteExistingBasePath_WhenAudAbsent()
        {
            var (chatContext, services) = BuildServices();
            chatContext.BasePath = "https://previous.example.com";
            var jwt = BuildJwt(aud: null);

            JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, services);

            Assert.Equal("https://previous.example.com", chatContext.BasePath);
        }
    }
}
