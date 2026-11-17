using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Events.ToolInstalled
{
    public class UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandler : INotificationHandler<MarketplaceToolInstalledDomainEvent>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public UpdateUserInstallationsMarketplaceToolInstalledDomainEventHandler(
            IMasterDataDbContext db,
            UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask Handle(MarketplaceToolInstalledDomainEvent notification, CancellationToken cancellationToken)
        {
            // Bug 7527: UserInstall is per-user "installed by me" bookkeeping. On install, a caller with no
            // resolved WKFLO user (an app-only token sent without the amais-wkflo-user-email header) has
            // nothing to record - skip it rather than failing the whole install on Guid.Parse(null). The
            // uninstall path below is unchanged: it only needs the email to remove an existing row.
            Guid userId = Guid.Empty;
            var hasResolvedUser = !string.IsNullOrWhiteSpace(_userContext.Email)
                && Guid.TryParse(_userContext.Guid, out userId)
                && userId != Guid.Empty;
            if (notification.Installations > 0 && !hasResolvedUser)
            {
                return;
            }

            var userInstall = await _db.UserInstalls
                .Where(us => us.Email == _userContext.Email &&
                           us.TypeId == notification.ToolId &&
                           us.Type == nameof(MarketplaceTool))
                .FirstOrDefaultAsync(cancellationToken);

            if (notification.Installations > 0)
            {
                if (userInstall == null)
                {
                    userInstall = new UserInstall
                    {
                        Email = _userContext.Email,
                        Type = nameof(MarketplaceTool),
                        TypeId = notification.ToolId,
                        UserId = userId
                    };
                    _db.UserInstalls.Add(userInstall);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    userInstall.UserId = userId;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            else if (notification.Installations < 0)
            {
                if (userInstall != null)
                {
                    _db.UserInstalls.Remove(userInstall);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}