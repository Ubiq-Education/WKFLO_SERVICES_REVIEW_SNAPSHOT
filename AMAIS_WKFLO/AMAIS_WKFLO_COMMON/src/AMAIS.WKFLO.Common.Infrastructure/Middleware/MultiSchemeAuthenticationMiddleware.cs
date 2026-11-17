using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    public class MultiSchemeAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAuthenticationSchemeProvider _schemeProvider;

        public MultiSchemeAuthenticationMiddleware(RequestDelegate next, IAuthenticationSchemeProvider schemeProvider)
        {
            _next = next;
            _schemeProvider = schemeProvider;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                // Try to authenticate with all available schemes
                var schemes = await _schemeProvider.GetAllSchemesAsync();

                foreach (var scheme in schemes)
                {
                    try
                    {
                        var result = await context.AuthenticateAsync(scheme.Name);
                        if (result.Succeeded && result.Principal?.Identity?.IsAuthenticated == true)
                        {
                            context.User = result.Principal;
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            await _next(context);
        }
    }
}
