using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    public class AutoUserCreationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AutoUserCreationMiddleware> _logger;

        public AutoUserCreationMiddleware(RequestDelegate next, ILogger<AutoUserCreationMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if user is authenticated and has email
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                try
                {
                    var userContext = context.RequestServices.GetRequiredService<UserContext>();

                    if (userContext != null && !string.IsNullOrWhiteSpace(userContext.Email))
                    {
                        var userRepository = context.RequestServices.GetService<IUserRepository>();

                        if (userRepository != null)
                        {
                            // Ensure user exists in database
                            var user = await userRepository.EnsureUserExistsAsync(userContext.Email, context.RequestAborted);
                            userContext.Guid = user.Id.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't break the request flow
                    _logger.LogError(ex, "Error in AutoUserCreationMiddleware");
                }
            }

            await _next(context);
        }
    }
}