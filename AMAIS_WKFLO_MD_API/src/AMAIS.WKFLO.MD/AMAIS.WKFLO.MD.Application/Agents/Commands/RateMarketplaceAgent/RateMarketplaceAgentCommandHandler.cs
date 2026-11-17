using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent
{
    /// <summary>
    /// Handler for rating a marketplace tool using UserStar entity
    /// </summary>
    public class RateMarketplaceAgentCommandHandler : IRequestHandler<RateMarketplaceAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;

        public RateMarketplaceAgentCommandHandler(IMasterDataDbContext db, UserContext userContext)
        {
            _db = db;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(RateMarketplaceAgentCommand command, CancellationToken cancellationToken)
        {
            // Verificar que la herramienta existe
            var marketplaceAgent = await _db.MarketplaceAgents
                .Where(m => m.Id == command.Id && !m.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (marketplaceAgent == null)
            {
                return new NotFoundResult(nameof(MarketplaceAgent), ErrorConst.AgentNotFound);
            }

            // Buscar si el usuario ya ha calificado esta herramienta
            var existingUserStar = await _db.UserStars
                .Where(us => us.Email == _userContext.Email &&
                            us.TypeId == command.Id &&
                            us.Type == nameof(MarketplaceAgent) &&
                            !us.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingUserStar != null)
            {
                existingUserStar.Value = command.Rating;
                existingUserStar.UserId = Guid.Parse(_userContext.Guid);
            }
            else
            {
                var newUserStar = new UserStar
                {
                    Email = _userContext.Email,
                    Type = nameof(MarketplaceAgent),
                    TypeId = command.Id,
                    Value = command.Rating,
                    UserId = Guid.Parse(_userContext.Guid)
                };
                await _db.UserStars.AddAsync(newUserStar, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<Boolean>(true);
        }
    }
}
