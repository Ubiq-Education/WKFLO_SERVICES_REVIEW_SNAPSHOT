using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteMarketplaceTool
{
    public class DeleteMarketplaceToolCommandHandler : IRequestHandler<DeleteMarketplaceToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteMarketplaceToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteMarketplaceToolCommand command, CancellationToken cancellationToken)
        {
            var marketplaceTool = await _db.MarketplaceTools.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (marketplaceTool == null)
            {
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);
            }

            marketplaceTool.IsDeleted = true;

            var publishedTenantTool = await _db.PublishedTenantTools.Where(m => m.Id == marketplaceTool.SourcePublishedToolId).FirstOrDefaultAsync();
            if (publishedTenantTool != null)
            {
                publishedTenantTool.MarketplaceToolId = null;
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceToolDeleteResult>(_mapper.Map<MarketplaceToolDeleteResult>(marketplaceTool));
        }
    }
}
