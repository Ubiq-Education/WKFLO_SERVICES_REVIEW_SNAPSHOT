using AMAIS.WKFLO.AI.Application.Extensions;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.HealthCheck;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.FunctionalTests
{
    /// <summary>
    /// Rebuilds the AI API's real request pipeline (AMAIS.WKFLO.AI.WebAPI/Program.cs) instead of
    /// reusing the shared <see cref="Setup"/>/<see cref="TestFixture"/>, which omits
    /// <see cref="MultiSchemeAuthenticationMiddleware"/> and never registers the
    /// <c>AmaisJwtScheme</c> at all. Bug 7163's claim ("UserContext.Guid is always Guid.Empty for
    /// widget chat, even with a valid Amais JWT") can only be verified against the exact
    /// middleware order production uses, so this fixture reproduces it deliberately.
    /// </summary>
    public class WidgetChatIdentitySetup(IConfiguration configuration)
    {
        public IConfiguration Configuration => configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDynamicCors();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(Configuration.GetSection("AzureAd"));

            // Mirrors Program.cs: a second, non-default scheme for the widget's custom "Amais JWT".
            // Real signing uses an RSA key from Key Vault (JwtKeyResolver) — tests override this via
            // PostConfigure with a symmetric key instead (see WidgetChatIdentityTestFixture).
            services.AddAuthentication()
                .AddJwtBearer(AmaisJwtConst.SchemeName, options => { });

            services.AddControllers(options =>
            {
                options.Filters.Add(typeof(HttpGlobalExceptionFilter));
                options.ModelValidatorProviders.Clear();
                options.Filters.Add(typeof(UserContextObservationFilter));
            })
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.TypeNameHandling = TypeNameHandling.None;
            })
            .AddApplicationPart(typeof(HealthCheckOK).Assembly);

            services.AddDomainTest();
            services.AddApplication(Configuration);
            services.AddInfrastructure(Configuration);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseCors(Configuration.GetSection(nameof(CorsSettings))["PolicyName"]);

            app.UseRouting();

            app.UseMiddleware<TenantContextResolver>();
            app.UseMiddleware<ChatContextResolver>();

            app.UseAuthentication();
            app.UseAuthorization();

            // Same order as the real Program.cs (AMAIS_WKFLO_AI_API/.../Program.cs lines 307-314).
            app.UseMiddleware<MultiSchemeAuthenticationMiddleware>();
            app.UseMiddleware<UserContextResolver>();
            app.UseAutoUserCreation();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }

    /// <summary>
    /// Test-only MVC action filter. Runs in the action-filter stage, which always executes after
    /// every authorization filter has completed — including <c>WebChatAuthorizationAttribute</c>'s
    /// JWT check — so it observes <c>UserContext</c>/<c>HttpContext.User</c> in exactly the state
    /// the real controller action would see them. Stashes that state into response headers so the
    /// test (which can't reach into the request's DI scope after the response completes) can assert
    /// on it directly, without touching any production code.
    /// </summary>
    public class UserContextObservationFilter : IAsyncActionFilter
    {
        public const string GuidHeader = "X-Test-UserContext-Guid";
        public const string EmailHeader = "X-Test-UserContext-Email";
        public const string AuthenticatedHeader = "X-Test-HttpContext-Authenticated";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var userContext = context.HttpContext.RequestServices.GetRequiredService<UserContext>();

            context.HttpContext.Response.Headers[GuidHeader] = userContext.Guid ?? string.Empty;
            context.HttpContext.Response.Headers[EmailHeader] = userContext.Email ?? string.Empty;
            context.HttpContext.Response.Headers[AuthenticatedHeader] =
                context.HttpContext.User.Identity?.IsAuthenticated == true ? "true" : "false";

            await next();
        }
    }

    /// <summary>
    /// Builds JWTs signed for the <c>AmaisJwtScheme</c> test key (see
    /// <see cref="WidgetChatIdentityTestFixture"/>), standing in for the real widget-issued Amais JWT.
    /// </summary>
    internal static class AmaisTestTokenBuilder
    {
        public const string Issuer = "AmaisWidgetTestIssuer";
        public const string SymmetricKey = "amais widget chat test signing key - functional tests only";

        public static string BuildToken(string email, string subject = null, string name = null)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SymmetricKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Email, email),
                new(ClaimTypes.NameIdentifier, subject ?? Guid.NewGuid().ToString())
            };

            if (!string.IsNullOrEmpty(name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
            }

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: null,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
